# Voice Command Manager
This project is origionally designed for Unity applications on Windows to realize the In Game Voice Control without native Speech Recognition function support in Unity. 

The program is a stand alone program that serves as listener to collect all commands from microphone, and dispatch to registered applications on the same machine via named pipe.

### Architecture
The program is composed by 2 major parts
- VoiceCmdRecognizer: build with .NET Speech Recognition library
- NamedPipeEventManager: used to communicate with registered applications

### Configuration

- Configuration sample for VoiceCmdManager

```sh
[
  {
    "channelName": "VideoPlayer",
    "cmdPipeName": "VoiceCmdManager.VideoPlayer.cmd",
    "ctrlPipeName": "VoiceCmdManager.VideoPlayer.ctrl",
    "grammars": [
      {
        "confidence": 0.5,
        "startWord": "video",
        "type": "main",
        "patterns": [ "play", "next", "previous", "go" ]
      },
      {
        "confidence": 0.5,
        "startWord": "video",
        "type": "videoplay",
        "patterns": [ "play", "pause", "stop", "forward", "backward" ]
      }
    ]
  },
  {
    "channelName": "VRDevice",
    "cmdPipeName": "VoiceCmdManager.VRDevice.cmd",
    "ctrlPipeName": "VoiceCmdManager.VRDevice.ctrl",
    "grammars": [
      {
        "confidence": 0.5,
        "startWord": "virtual reality",
        "type": "teleport",
        "patterns": [ "play", "gallery", "news", "scene" ]
      }
    ]
  }]
```

- Configuration sample for client
```sh
{
	"channelName": "VideoPlayer",
	"cmdPipeName": "VoiceCmdManager.VideoPlayer.cmd",
	"ctrlPipeName": "VoiceCmdManager.VideoPlayer.ctrl",
	"grammars": [{
		"type": "main",
		"patterns": ["next", "previous", "go"]
	}, {
		"type": "videoplay",
		"patterns": ["play", "pause", "stop", "forward", "backward"]
	}]
}
```

### Usage




