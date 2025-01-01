using System;
using System.Collections.Generic; // for List<T>, Queue<T>
using System.Drawing;
using System.Windows.Forms;
using System.Net;
using System.Threading.Tasks;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;

/// <summary>
/// Simple logger class that appends to a text file.
/// </summary>
public static class Logger
{
    public static void Log(object message)
    {
        File.AppendAllText("AiAgent.log", DateTime.Now + " : " + message + Environment.NewLine);
    }
}

/// <summary>
/// The types of actions our agent can execute
/// </summary>
public enum AgentActionType
{
    Move,
    Punch,
    Idle
}

/// <summary>
/// A container for an action + optional parameters (like a target position).
/// </summary>
public class AgentAction
{
    public AgentActionType ActionType;
    public Vector3 TargetPosition;  // For move
    public long StartTime;          // For timing logic
}

public class AiAgentLongTerm : Script
{
    private const string GROQ_API_KEY = "YOUR_GROQ_KEY";  // Replace with your Groq/OpenAI key
    private const string API_URL = "https://api.groq.com/openai/v1/chat/completions";

    // We’ll poll for new instructions if the action queue is empty
    private int aiCheckIntervalMs = 1000; // 1 second
    private long lastCheckTime = 0;
    private bool isProcessingRequest = false;

    // A queue of steps from the LLM
    private Queue<AgentAction> actionQueue = new Queue<AgentAction>();
    private AgentAction currentAction = null;

    // "Memory": We record each final position after an action is completed
    private List<Vector3> visitedPositions = new List<Vector3>();

    public AiAgentLongTerm()
    {
        Tick += OnTick;
        ConfigureSecurityProtocol();

        Logger.Log("AiAgentLongTerm: script loaded and initialized.");
    }

    private void ConfigureSecurityProtocol()
    {
        try
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 |
                                                   SecurityProtocolType.Tls11 |
                                                   SecurityProtocolType.Tls;
            ServicePointManager.ServerCertificateValidationCallback =
                ((sender, certificate, chain, sslPolicyErrors) => true);

            Logger.Log("Security protocols configured successfully.");
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to configure security protocols: " + ex.Message);
        }
    }

    private void OnTick(object sender, EventArgs e)
    {
        // If we have a current action, check if it's done
        if (currentAction != null)
        {
            if (!IsActionDone(currentAction))
            {
                // Still performing it
                return;
            }
            else
            {
                // Action is done => record final position + log
                Vector3 finalPos = Game.Player.Character.Position;
                visitedPositions.Add(finalPos);
                Logger.Log("Action completed: " + currentAction.ActionType + 
                           ". Final pos: " + finalPos.ToString());
                currentAction = null;
            }
        }

        // If there's another action in queue, pop it + start it
        if (actionQueue.Count > 0)
        {
            currentAction = actionQueue.Dequeue();
            currentAction.StartTime = Game.GameTime;  // record time we started
            Logger.Log("Starting new queued action: " + currentAction.ActionType);
            StartAction(currentAction);
            return;
        }

        // If queue is empty, request more instructions from the LLM
        if (!isProcessingRequest && (Game.GameTime - lastCheckTime >= aiCheckIntervalMs))
        {
            lastCheckTime = Game.GameTime;
            isProcessingRequest = true;
            Task.Run(async () => await MakeAIRequest());
        }
    }

    /// <summary>
    /// Creates the system prompt referencing the environment, goal, and visited positions.
    /// </summary>
    private string CreateAgentPrompt()
    {
        Ped player = Game.Player.Character;
        Vector3 pos = player.Position;
        float heading = player.Heading;
        int health = player.Health;
        int armor = player.Armor;
        int wanted = Game.Player.WantedLevel;
        string weather = World.Weather.ToString();

        // The goal = Los Santos International Airport coords
        Vector3 airportCoords = new Vector3(-1034.6f, -2733.6f, 20.2f);

        // Summarize visited positions
        string visitedInfo = "";
        if (visitedPositions.Count > 0)
        {
            visitedInfo = "You have previously visited these coords:\n";
            foreach (var v in visitedPositions)
            {
                visitedInfo += " - " + v.ToString() + "\n";
            }
        }
        else
        {
            visitedInfo = "You have not visited any previous coords yet.\n";
        }

        // Summarize environment info
        string envInfo =
            "Current environment info:\n" +
            " - Position: " + pos.ToString() + "\n" +
            " - Heading: " + heading.ToString("F1") + "\n" +
            " - Health: " + health + "\n" +
            " - Armor: " + armor + "\n" +
            " - Wanted: " + wanted + "\n" +
            " - Weather: " + weather + "\n";

        // Nearest ped
        Ped nearestPed = FindNearestPed(50f);
        string nearestPedStr = (nearestPed != null)
            ? " - Nearest Ped at " + nearestPed.Position.ToString()
            : " - No ped nearby";

        // Because we removed vehicles, we won't list them
        // Or we can keep them as reference but we won't do "enter_vehicle" anymore
        // For clarity, let's remove it entirely:

        string systemContent =
            "You are an AI controlling a GTA V character. " +
            "Your primary goal is to reach Los Santos International Airport at approximate coords " +
            "(-1034.6, -2733.6, 20.2).\n\n" +
            envInfo +
            nearestPedStr + "\n\n" +
            visitedInfo + "\n" +
            "Actions you can output (one per line):\n" +
            " move(x,y,z)\n" +
            " punch()\n" +
            " idle()\n\n" +
            "You may provide up to 3 lines, each line is one action. " +
            "Focus on moving closer or eventually arriving at your goal.\n" +
            "No extra text, no JSON, only these lines.\n";

        // Construct the final JSON
        string request =
            "{" +
            "\"messages\": [" +
            "  {\"role\": \"system\", \"content\": \"" + EscapeForJson(systemContent) + "\"}," +
            "  {\"role\": \"user\", \"content\": \"Plan up to 3 actions.\"}" +
            "]," +
            "\"model\": \"llama-3.3-70b-versatile\"," +
            "\"temperature\": 0.7," +
            "\"max_tokens\": 100," +
            "\"stream\": false" +
            "}";

        return request;
    }

    private string EscapeForJson(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    private async Task MakeAIRequest()
    {
        try
        {
            Logger.Log("Starting AI request...");
            using (var client = new WebClient())
            {
                client.Headers.Add("Content-Type", "application/json");
                client.Headers.Add("Authorization", "Bearer " + GROQ_API_KEY);
                client.Headers.Add("User-Agent", "GTA5Mod/1.0");

                string requestData = CreateAgentPrompt();
                Logger.Log("Request data: " + requestData);

                string response = await client.UploadStringTaskAsync(API_URL, requestData);
                Logger.Log("Raw LLM response: " + response);

                string content = ExtractAssistantContent(response);
                Logger.Log("Extracted LLM content:\n" + content);

                ParseActions(content);
            }
        }
        catch (WebException wex)
        {
            string errorBody = "";
            HttpWebResponse httpResp = wex.Response as HttpWebResponse;
            if (httpResp != null)
            {
                Logger.Log("HTTP Status: " + (int)httpResp.StatusCode + " " + httpResp.StatusCode);
                using (Stream respStream = httpResp.GetResponseStream())
                {
                    if (respStream != null)
                    {
                        using (StreamReader reader = new StreamReader(respStream))
                        {
                            errorBody = reader.ReadToEnd();
                        }
                    }
                }
            }
            Logger.Log("Error in MakeAIRequest: " + wex.Message);
            if (!string.IsNullOrEmpty(errorBody))
            {
                Logger.Log("Server returned error body: " + errorBody);
            }
            Logger.Log("Stack trace: " + wex.StackTrace);
        }
        catch (Exception ex)
        {
            Logger.Log("Error in MakeAIRequest: " + ex.Message);
            Logger.Log("Stack trace: " + ex.StackTrace);
        }
        finally
        {
            isProcessingRequest = false;
            Logger.Log("AI request process completed.");
        }
    }

    private string ExtractAssistantContent(string response)
    {
        int roleAssistantIndex = response.IndexOf("\"role\":\"assistant\"");
        if (roleAssistantIndex == -1)
            return "";

        int contentIndex = response.IndexOf("\"content\":", roleAssistantIndex);
        if (contentIndex == -1)
            return "";

        int startQuote = response.IndexOf("\"", contentIndex + 10);
        if (startQuote == -1)
            return "";

        int endQuote = response.IndexOf("\"", startQuote + 1);
        if (endQuote == -1)
            return "";

        string content = response.Substring(startQuote + 1, endQuote - (startQuote + 1));
        content = content
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\\\"", "\"");
        return content.Trim();
    }

    /// <summary>
    /// Parses the LLM’s text. Each line can be:
    ///   move(x,y,z)
    ///   punch()
    ///   idle()
    /// We enqueue them in actionQueue.
    /// </summary>
    private void ParseActions(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            Logger.Log("No content from LLM, no new actions queued.");
            return;
        }

        var lines = content.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            string lower = line.Trim().ToLower();
            if (lower.StartsWith("idle"))
            {
                var act = new AgentAction { ActionType = AgentActionType.Idle };
                actionQueue.Enqueue(act);
                Logger.Log("Queued: idle()");
            }
            else if (lower.StartsWith("punch"))
            {
                var act = new AgentAction { ActionType = AgentActionType.Punch };
                actionQueue.Enqueue(act);
                Logger.Log("Queued: punch()");
            }
            else if (lower.StartsWith("move(") && lower.Contains(")"))
            {
                int openParen = lower.IndexOf("(");
                int closeParen = lower.IndexOf(")", openParen);
                if (closeParen > openParen)
                {
                    string inside = line.Substring(openParen + 1, closeParen - (openParen + 1));
                    // e.g. "100,200,30"
                    var parts = inside.Split(',');
                    if (parts.Length == 3)
                    {
                        float x, y, z;
                        bool okX = float.TryParse(parts[0], out x);
                        bool okY = float.TryParse(parts[1], out y);
                        bool okZ = float.TryParse(parts[2], out z);
                        if (okX && okY && okZ)
                        {
                            var act = new AgentAction { ActionType = AgentActionType.Move, TargetPosition = new Vector3(x, y, z) };
                            actionQueue.Enqueue(act);
                            Logger.Log("Queued: move(" + x + "," + y + "," + z + ")");
                        }
                    }
                }
            }
            else
            {
                Logger.Log("Unrecognized line from LLM: " + line);
            }
        }
    }

    /// <summary>
    /// Called when we actually begin an action. We do the necessary tasks or natives.
    /// </summary>
    private void StartAction(AgentAction action)
    {
        Ped player = Game.Player.Character;

        switch (action.ActionType)
        {
            case AgentActionType.Move:
                Logger.Log("Starting Move to " + action.TargetPosition.ToString());
                player.Task.RunTo(action.TargetPosition, false, -1);
                break;

            case AgentActionType.Punch:
                Logger.Log("Starting Punch action");
                Ped pedToPunch = FindNearestPed(5f);
                if (pedToPunch != null)
                {
                    // Use Task.Combat in SHVDN 3.7+ if we want to engage in melee
                    player.Task.Combat(pedToPunch);
                }
                else
                {
                    // Air swing
                    player.Task.PlayAnimation("melee@unarmed@streamed_core_fps", "ground_attack_on_spot",
                        8.0f, -8.0f, 1000, (AnimationFlags)0, 0.0f);
                }
                break;

            case AgentActionType.Idle:
            default:
                Logger.Log("Starting Idle action (no operation).");
                break;
        }
    }

    /// <summary>
    /// Checks if the action is done. 
    /// Move => done if within 1.3 units or after 10 seconds.
    /// Punch => done if not in combat or after 2 seconds.
    /// Idle => immediate.
    /// </summary>
    private bool IsActionDone(AgentAction action)
    {
        Ped player = Game.Player.Character;
        long elapsed = Game.GameTime - action.StartTime;

        switch (action.ActionType)
        {
            case AgentActionType.Move:
            {
                float dist = player.Position.DistanceTo(action.TargetPosition);
                if (dist < 1.3f)
                    return true;
                if (elapsed > 10000)  // 10s fallback
                {
                    Logger.Log("Move action timed out after 10s; marking as done.");
                    return true;
                }
                return false;
            }

            case AgentActionType.Punch:
            {
                bool inCombat = player.IsInCombat;
                if (!inCombat || elapsed > 2000)
                    return true;
                return false;
            }

            case AgentActionType.Idle:
            default:
                return true;
        }
    }

    private Ped FindNearestPed(float radius)
    {
        Ped player = Game.Player.Character;
        Ped[] nearPeds = World.GetNearbyPeds(player, radius);
        Ped nearest = null;
        float minDist = float.MaxValue;
        foreach (var p in nearPeds)
        {
            if (p != null && p.Exists() && p != player)
            {
                float d = p.Position.DistanceTo(player.Position);
                if (d < minDist)
                {
                    minDist = d;
                    nearest = p;
                }
            }
        }
        return nearest;
    }
}
