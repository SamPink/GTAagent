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
                
                // Start driving to airport
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
            if (driver != null && currentVehicle != null) {
                // Clear any existing tasks
                driver.Task.ClearAll();
                
                // Set driving style flags
                int drivingStyle = 447; // Normal driving style + avoid obstacles
                float cruiseSpeed = 20f; // Speed in m/s (about 45mph)
                
                // Start the driving task
                Function.Call(Hash.TASK_VEHICLE_DRIVE_TO_COORD,
                    driver.Handle,                 // Ped handle
                    currentVehicle.Handle,         // Vehicle handle
                    AIRPORT_DESTINATION.X,         // X coordinate
                    AIRPORT_DESTINATION.Y,         // Y coordinate
                    AIRPORT_DESTINATION.Z,         // Z coordinate
                    cruiseSpeed,                   // Speed
                    1.0f,                         // Don't stop at destination
                    currentVehicle.Model.Hash,     // Vehicle hash
                    drivingStyle,                  // Driving style
                    5.0f,                         // Stop range
                    20.0f                         // Straighten out range
                );
                
                Logger.Log("Set driving task to airport");
            }
        } catch (Exception ex) {
            Logger.Log("Error in DriveToDestination: " + ex.Message);
        }
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