using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.IO;
using GTA;
using GTA.Math;
using GTA.Native;

public class AutoDriver : Script {
    private Vehicle currentVehicle;
    private bool isActive = false;
    private long lastUpdateTime = 0;
    private int updateInterval = 1000;
    private readonly Vector3 AIRPORT_DESTINATION = new Vector3(-1034.6f, -2733.6f, 20.2f);
    
    public AutoDriver() {
        Tick += OnTick;
        KeyDown += OnKeyDown;
        Logger.Log("AutoDriver script initialized");
    }
    
    private void OnKeyDown(object sender, KeyEventArgs e) {
        try {
            if (e.KeyCode == Keys.NumPad1) {
                Logger.Log("Numpad 1 pressed");
                if (!isActive) {
                    StartDriving();
                } else {
                    StopDriving();
                }
            }
        } catch (Exception ex) {
            Logger.Log("Error in OnKeyDown: " + ex.Message);
        }
    }
    
    private void StartDriving() {
        try {
            Logger.Log("StartDriving called");
            
            if (currentVehicle != null && currentVehicle.Exists()) {
                currentVehicle.Delete();
            }
            
            Vector3 spawnPos = Game.Player.Character.Position + (Game.Player.Character.ForwardVector * 5f);
            currentVehicle = World.CreateVehicle(VehicleHash.Zentorno, spawnPos);
            
            if (currentVehicle != null && currentVehicle.Exists()) {
                Game.Player.Character.SetIntoVehicle(currentVehicle, VehicleSeat.Driver);
                lastUpdateTime = Game.GameTime;
                isActive = true;
                
                DriveToDestination();
                
                Logger.Log("Vehicle created and driving task started");
            }
            
        } catch (Exception ex) {
            Logger.Log("Error in StartDriving: " + ex.Message);
        }
    }
    
    private void StopDriving() {
        try {
            isActive = false;
            if (currentVehicle != null && currentVehicle.Exists()) {
                Game.Player.Character.Task.ClearAll();
            }
            Logger.Log("Driving stopped");
        } catch (Exception ex) {
            Logger.Log("Error in StopDriving: " + ex.Message);
        }
    }

    private void DriveToDestination() {
        try {
            Ped driver = Game.Player.Character;
            if (driver == null || currentVehicle == null) {
                Logger.Log("Driver or vehicle is null");
                return;
            }

            // Clear any existing tasks first
            driver.Task.ClearAll();
            
            // Calculate driving parameters based on environment and conditions
            float cruiseSpeed = CalculateOptimalSpeed();
            int drivingStyle = CalculateDrivingStyle();
            
            // Create waypoint path to destination
            var waypoints = GenerateWaypoints(currentVehicle.Position, AIRPORT_DESTINATION);
            
            foreach (var waypoint in waypoints) {
                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD,
                    driver.Handle,             // Ped handle
                    currentVehicle.Handle,     // Vehicle handle
                    waypoint.X,                // X coordinate
                    waypoint.Y,                // Y coordinate
                    waypoint.Z,                // Z coordinate
                    cruiseSpeed,               // Speed
                    1.0f,                      // Stop at end
                    currentVehicle.Model.Hash, // Vehicle model hash
                    drivingStyle,              // Driving style
                    5.0f,                      // Stop range
                    20.0f                      // Straighten out range
                );
                
                // Monitor progress to this waypoint
                while (isActive && currentVehicle.Position.DistanceTo(waypoint) > 5.0f) {
                    Script.Wait(100);
                    AdjustDriving();
                }
            }
        }
        catch (Exception ex) {
            Logger.Log("Error in DriveToDestination: " + ex.Message);
        }
    }

    private float CalculateOptimalSpeed() {
        // Base speed in m/s
        float baseSpeed = 20f;
        
        // Adjust speed based on road type and conditions
        if (IsHighway()) {
            baseSpeed = 35f; // About 78 mph
        }
        else if (IsInCity()) {
            baseSpeed = 15f; // About 33 mph
        }
        
        // Reduce speed in rain or at night
        if (World.Weather == Weather.Raining) {
            baseSpeed *= 0.8f;
        }
        if (World.CurrentTimeOfDay.Hours < 6 || World.CurrentTimeOfDay.Hours > 20) {
            baseSpeed *= 0.9f;
        }
        
        return baseSpeed;
    }

    private int CalculateDrivingStyle() {
        // Combine different driving style flags
        int style = 0;
        
        // Base flags
        style |= 447;      // Normal driving
        style |= 262144;   // Avoid vehicles
        style |= 2883621;  // Avoid empty vehicles
        style |= 786603;   // Avoid objects
        
        // Add more cautious behavior in certain conditions
        if (World.Weather == Weather.Raining || 
            World.CurrentTimeOfDay.Hours < 6 || 
            World.CurrentTimeOfDay.Hours > 20) {
            style |= 524288;   // Increased stopping distance
        }
        
        return style;
    }

    private List<Vector3> GenerateWaypoints(Vector3 start, Vector3 end) {
        var waypoints = new List<Vector3>();
        
        // Calculate total distance
        float totalDistance = start.DistanceTo(end);
        int numWaypoints = (int)(totalDistance / 100.0f); // One waypoint every 100 units
        
        for (int i = 0; i <= numWaypoints; i++) {
            float progress = (float)i / numWaypoints;
            Vector3 waypoint = Vector3.Lerp(start, end, progress);
            
            // Get nearest vehicle node
            OutputArgument outPos = new OutputArgument();
            Function.Call(Hash.GET_NTH_CLOSEST_VEHICLE_NODE,
                waypoint.X, waypoint.Y, waypoint.Z,
                1, // Get closest node
                outPos,
                0, 0, 0);
            
            waypoints.Add(outPos.GetResult<Vector3>());
        }
        
        return waypoints;
    }

    private void AdjustDriving() {
        if (currentVehicle == null) return;
        
        // Check for obstacles
        var forwardVector = currentVehicle.ForwardVector;
        var raycastResult = World.Raycast(
            currentVehicle.Position,
            currentVehicle.Position + (forwardVector * 10f),
            IntersectFlags.Everything
        );
        
        if (raycastResult.DidHit) {
            // Temporary speed reduction for obstacles
            Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED, 
                Game.Player.Character.Handle, 
                currentVehicle.Speed * 0.5f);
                
            Script.Wait(1000);
        }
        
        // Check for dangerous turns
        if (IsSharpTurnAhead()) {
            Function.Call(Hash.SET_DRIVE_TASK_CRUISE_SPEED,
                Game.Player.Character.Handle,
                10f); // Slow down for turns
        }
    }

    private bool IsHighway() {
        // Get zone name at current position
        string zoneName = Function.Call<string>(Hash.GET_NAME_OF_ZONE,
            currentVehicle.Position.X,
            currentVehicle.Position.Y,
            currentVehicle.Position.Z);
            
        return zoneName.Contains("HIGHWAY") || zoneName.Contains("FREEWAY");
    }

    private bool IsInCity() {
        // Get zone name at current position
        string zoneName = Function.Call<string>(Hash.GET_NAME_OF_ZONE,
            currentVehicle.Position.X,
            currentVehicle.Position.Y,
            currentVehicle.Position.Z);
            
        return zoneName.Contains("CITY") || zoneName.Contains("DOWNTOWN");
    }

    private bool IsSharpTurnAhead() {
        // Ray cast ahead to detect road curvature
        var forwardPos = currentVehicle.Position + (currentVehicle.ForwardVector * 20f);
        
        OutputArgument outPos = new OutputArgument();
        Function.Call(Hash.GET_NTH_CLOSEST_VEHICLE_NODE,
            forwardPos.X, forwardPos.Y, forwardPos.Z,
            1,
            outPos,
            0, 0, 0);
            
        Vector3 roadNode = outPos.GetResult<Vector3>();
        
        // Calculate angle between current direction and road ahead
        var directionToNode = (roadNode - currentVehicle.Position).Normalized;
        float angle = Math.Abs(Vector3.Angle(currentVehicle.ForwardVector, directionToNode));
        
        return angle > 45f;
    }
    
    private void OnTick(object sender, EventArgs e) {
        try {
            if (!isActive) return;
            
            if (currentVehicle == null || !currentVehicle.Exists()) {
                StopDriving();
                return;
            }
            
            // Log state periodically
            if (Game.GameTime - lastUpdateTime >= updateInterval) {
                lastUpdateTime = Game.GameTime;
                LogVehicleState();
                
                // Check if we need to update the driving task
                float distanceToAirport = currentVehicle.Position.DistanceTo(AIRPORT_DESTINATION);
                if (distanceToAirport < 10f) {
                    Logger.Log("Reached destination!");
                    StopDriving();
                }
            }
            
        } catch (Exception ex) {
            Logger.Log("Error in OnTick: " + ex.Message);
            StopDriving();
        }
    }
    
    private void LogVehicleState() {
        try {
            float distanceToAirport = currentVehicle.Position.DistanceTo(AIRPORT_DESTINATION);
            
            string stateInfo = string.Format(
                "Vehicle State:\n" +
                "Position: {0}\n" +
                "Velocity: {1}\n" +
                "Speed: {2:F2} m/s\n" +
                "Heading: {3:F2} degrees\n" +
                "Distance to Airport: {4:F1}m",
                currentVehicle.Position,
                currentVehicle.Velocity,
                currentVehicle.Speed,
                currentVehicle.Heading,
                distanceToAirport
            );
            Logger.Log(stateInfo);
        } catch (Exception ex) {
            Logger.Log("Error logging vehicle state: " + ex.Message);
        }
    }
}

public static class Logger {
    public static void Log(string message) {
        try {
            File.AppendAllText("AutoDriver.log", 
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " : " + message + Environment.NewLine);
        } catch {
            // Ignore logging errors
        }
    }
}