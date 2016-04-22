# Voice Command Manager
This project is origionally designed for Unity applications on Windows to realize the In Game Voice Control without native Speech Recognition function support in Unity. 

The program is a stand alone program that serves as listener to collect all commands from microphone, and dispatch to registered applications on the same machine via named pipe.

### Architecture
The program is composed by 2 major parts
- VoiceCmdRecognizer: build with .NET Speech Recognition library
- NamedPipeEventManager: used to communicate with registered applications

### Configuration

#### Configuration Definitions

```sh
[
  {
    "channelName": "string, used to dispatch command to correct application",
    "cmdPipeName": "string, used for the named pipe that send voice command to application",
    "ctrlPipeName": "string, used for the named pipe that receive the control message from application, such as pattern update",
    "grammars": [
      {
        "confidence": float, // confidence level used to filter inaccurate result
        "startWord": "string, the prefix of patterns",
        "type": "string, defined by client to separate patterns that application uses",
        "patterns": [ "string array of patterns", "next", "previous", "go" ]
      }
    ]
  }
]
```
- How startWord works
The startWord is the prefix of patterns. For example, if you have a startWord "video" and a pattern "play", the real phrase that will be recognised is "video play". This is generally used to reduce the matched result for common words, and also improve the accuracy since the longer the pattern is, the more accurate the result will be. 

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

### Testing
* Build the project with Visual Studio
* Under bin folder there suppose to be 2 binaries
  * VoiceCmdManager.exe: the voice command manager that listen and dispatch results to registered application
  * VoiceCmdTestClient.ext: the test program that simulates the application's behavior
* Launch VoiceCmdManager.exe, it will load VoiceCmdSetting.json as config file.
* Launch VoiceCmdTestClient.exe, it will load VoiceCmdClientSetting.json as config file.
* Say the patterns you put in the VoiceCmdSetting.json file, such as "video play"
  * you should see the command recognised and received in the console of VoiceCmdTestClient.exe



