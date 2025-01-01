# GTA V AI Agent Mod

This is a sophisticated AI-powered mod for Grand Theft Auto V that creates an autonomous agent capable of navigating through Los Santos using Large Language Model (LLM) integration.

## Core Features

**AI-Driven Navigation**
The mod implements an AI agent that autonomously navigates towards the Los Santos International Airport using a queue-based action system[1].

**Available Actions**
- Move to coordinates (x,y,z)
- Punch nearby NPCs
- Idle state

## Technical Implementation

**Key Components**
- Action Queue System: Manages a sequence of AI-determined actions
- Position Memory: Tracks previously visited locations
- Environmental Awareness: Monitors player status, nearby NPCs, and weather conditions

**Action Processing**
```csharp
public enum AgentActionType {
    Move,
    Punch,
    Idle
}
```

## Configuration

**API Setup**
The mod requires a Groq API key for LLM integration:
```csharp
private const string GROQ_API_KEY = "your_api_key_here"
private const string API_URL = "https://api.groq.com/openai/v1/chat/completions"
```

## How It Works

1. The agent continuously monitors its environment
2. When no action is being performed, it requests new instructions from the LLM
3. The LLM provides up to 3 actions based on current conditions
4. Actions are executed sequentially from the queue

## Installation

1. Ensure you have the latest version of GTA V
2. Install the required Script Hook V
3. Place the mod files in your GTA V scripts folder
4. Configure your API key in the source code

## Dependencies

- GTA V
- Script Hook V
- .NET Framework
- Active internet connection for LLM integration

## Technical Notes

The mod implements robust error handling and logging functionality for debugging purposes. All actions and API responses are logged to `AiAgent.log`[1].

## Limitations

- Actions are limited to movement, combat, and idle states
- Navigation is optimized for reaching the airport location
- Each action has specific timeout periods to prevent hanging
