using System;
using System.Drawing;
using System.Windows.Forms;
using System.Net;
using System.Threading.Tasks;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;

public static class Logger
{
    public static void Log(object message)
    {
        File.AppendAllText("AiAgent.log", DateTime.Now + " : " + message + Environment.NewLine);
    }
}

public class AiAgent : Script
{
    private const string GROQ_API_KEY = "";  // <-- Replace with your actual API key
    private const string API_URL = "https://api.groq.com/openai/v1/chat/completions";

    // We'll request an action from the LLM every X ms
    private int aiRequestIntervalMs = 1500; // 15 seconds
    private long lastRequestTime = 0;

    private bool isProcessingRequest = false;

    // After the LLM responds, we store the parsed action:
    private bool shouldMove = false;
    private Vector3 targetPos = Vector3.Zero;
    private bool shouldIdle = false;

    public AiAgent()
    {
        Tick += OnTick;
        ConfigureSecurityProtocol();

        Logger.Log("AiAgent: Self-operating script loaded and initialized.");
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
        // 1) If we have a pending action, execute it
        if (shouldMove)
        {
            ExecuteMove(targetPos);
            shouldMove = false;
            targetPos = Vector3.Zero;
        }
        else if (shouldIdle)
        {
            Logger.Log("Executing idle action.");
            shouldIdle = false;
        }

        // 2) Periodically call LLM
        if (!isProcessingRequest && (Game.GameTime - lastRequestTime >= aiRequestIntervalMs))
        {
            lastRequestTime = Game.GameTime;
            isProcessingRequest = true;
            Task.Run(async () => await MakeAIRequest());
        }
    }

    /// <summary>
    /// Builds a minimal text prompt instructing the LLM
    /// to respond with either move(x,y,z) or idle, based on environment data.
    /// </summary>
    private string CreateAgentPrompt()
    {
        Ped player = Game.Player.Character;
        Vector3 playerPos = player.Position;
        float heading = player.Heading;
        int health = player.Health;
        int wanted = Game.Player.WantedLevel;
        string weather = World.Weather.ToString();

        // Summarize environment in a multiline string (we'll escape newlines below)
        string systemContent =
            "You are an AI controlling a character in GTA V. Environment info:\n" +
            " - Position: " + playerPos.ToString() + "\n" +
            " - Heading: " + heading.ToString("F1") + "\n" +
            " - Health: " + health + "\n" +
            " - Wanted Level: " + wanted + "\n" +
            " - Weather: " + weather + "\n\n" +
            "Respond with EXACTLY one line of text: either\n" +
            "  move(x,y,z)\n" +
            "or\n" +
            "  idle\n\n" +
            "Coordinates in move(x,y,z) should be near the player if you want to move.\n" +
            "No extra text, no quotes, no JSON, just the instruction like move(123.45,678.90,30.0) or idle.";

        // Build final request data as a JSON string.
        // We'll run the entire systemContent through EscapeForJson, which now
        // also replaces newline characters with \\n
        string request =
            "{" +
            "\"messages\": [" +
            "  {" +
            "    \"role\": \"system\"," +
            "    \"content\": \"" + EscapeForJson(systemContent) + "\"" +
            "  }," +
            "  {" +
            "    \"role\": \"user\"," +
            "    \"content\": \"What should we do next?\"" +
            "  }" +
            "]," +
            "\"model\": \"llama-3.3-70b-versatile\"," +
            "\"temperature\": 0.7," +
            "\"max_tokens\": 50," +
            "\"stream\": false" +
            "}";

        return request;
    }

    /// <summary>
    /// Escapes quotes, backslashes, and also replaces newline / carriage returns
    /// with their JSON-friendly forms (\\n, \\r).
    /// This prevents the Groq server from choking on raw newlines.
    /// </summary>
    private string EscapeForJson(string text)
    {
        return text
            .Replace("\\", "\\\\")   // escape backslashes
            .Replace("\"", "\\\"")   // escape quotes
            .Replace("\r", "\\r")    // escape carriage returns
            .Replace("\n", "\\n");   // escape new lines
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

                // Post to the Groq endpoint
                string response = await client.UploadStringTaskAsync(API_URL, requestData);
                Logger.Log("Raw LLM response: " + response);

                // Extract the "assistant" content
                string content = ExtractAssistantContent(response);
                Logger.Log("Extracted LLM content: " + content);

                // Parse either move(...) or idle
                ParseAndStoreAction(content);
            }
        }
        catch (WebException wex)
        {
            string errorBody = "";
            HttpWebResponse httpResp = wex.Response as HttpWebResponse;

            if (httpResp != null)
            {
                Logger.Log("HTTP Status: " + (int)httpResp.StatusCode + " " + httpResp.StatusCode);

                // Attempt to read the error response
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

            // Default to idle if there's an error
            shouldIdle = true;
        }
        catch (Exception ex)
        {
            Logger.Log("Error in MakeAIRequest: " + ex.Message);
            Logger.Log("Stack trace: " + ex.StackTrace);

            // Default to idle if there's an error
            shouldIdle = true;
        }
        finally
        {
            isProcessingRequest = false;
            Logger.Log("AI request process completed.");
        }
    }

    /// <summary>
    /// Naive approach to find the assistant's content in the returned JSON
    /// without using a JSON library.
    /// </summary>
    private string ExtractAssistantContent(string response)
    {
        // We look for something like:  "role":"assistant","content":"move(123,456,30)"
        int roleAssistantIndex = response.IndexOf("\"role\":\"assistant\"");
        if (roleAssistantIndex == -1)
        {
            return "idle";
        }

        int contentIndex = response.IndexOf("\"content\":", roleAssistantIndex);
        if (contentIndex == -1)
        {
            return "idle";
        }

        int startQuote = response.IndexOf("\"", contentIndex + 10);
        if (startQuote == -1)
        {
            return "idle";
        }

        int endQuote = response.IndexOf("\"", startQuote + 1);
        if (endQuote == -1)
        {
            return "idle";
        }

        string content = response.Substring(startQuote + 1, endQuote - (startQuote + 1));
        // Undo JSON-escaping for quotes, newlines, etc. if needed
        content = content
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\\\"", "\"");
        return content.Trim();
    }

    /// <summary>
    /// If content is "move(x,y,z)", parse coords; if "idle", set idle; else idle by default.
    /// </summary>
    private void ParseAndStoreAction(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            Logger.Log("LLM content empty, defaulting to idle.");
            shouldIdle = true;
            return;
        }

        string lower = content.ToLower();
        if (lower.StartsWith("idle"))
        {
            Logger.Log("LLM said idle.");
            shouldIdle = true;
            return;
        }

        // Attempt to parse move(x,y,z)
        if (lower.Contains("move(") && lower.Contains(")"))
        {
            int openParen = lower.IndexOf("(");
            int closeParen = lower.IndexOf(")", openParen);
            if (closeParen > openParen)
            {
                string inside = content.Substring(openParen + 1, closeParen - (openParen + 1));
                // inside might be  "100.1,200.2,30.3"
                string[] parts = inside.Split(',');
                if (parts.Length == 3)
                {
                    float x, y, z;
                    bool okX = float.TryParse(parts[0], out x);
                    bool okY = float.TryParse(parts[1], out y);
                    bool okZ = float.TryParse(parts[2], out z);

                    if (okX && okY && okZ)
                    {
                        Logger.Log("LLM said move => " + x + ", " + y + ", " + z);
                        targetPos = new Vector3(x, y, z);
                        shouldMove = true;
                        return;
                    }
                }
            }
        }

        // If we get here, we couldn't parse => default to idle
        Logger.Log("LLM content unrecognized, defaulting to idle.");
        shouldIdle = true;
    }

    /// <summary>
    /// Called in OnTick for "move" instructions.
    /// We use Task.RunTo(...) so we don't rely on an unavailable GoTo() overload.
    /// </summary>
    private void ExecuteMove(Vector3 destination)
    {
        Logger.Log("Executing move action to " + destination.ToString());
        Ped player = Game.Player.Character;

        // We'll have the ped run to the destination
        player.Task.RunTo(destination, false, -1);
    }
}
