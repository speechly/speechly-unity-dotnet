using System;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.IO;

namespace Speechly.SLUClient {

  public enum ClientState {
    Failed = 0,
    NoBrowserSupport,
    NoAudioConsent,
    __UnrecoverableErrors,
    Disconnected,
    Disconnecting,
    Connecting,
    Preinitialized,
    Initializing,
    Connected,
    Stopping,
    Starting,
    Recording,
  }


  public interface IMsgCommonProps {
    string type { get; set; }
    string audio_context { get; set; }
    int segment_id { get; set; }
  }

  [DataContract]
  public class Word {
    [DataMember(Name = "word")]
    public string word;
    [DataMember(Name = "index")]
    public int index;
    [DataMember(Name = "start_timestamp")]
    public int startTimestamp;
    [DataMember(Name = "end_timestamp")]
    public int endTimestamp;
    public bool isFinal = false;
  }

  [DataContract]
  public class Entity {
    [DataMember(Name = "entity")]
    public string type;
    [DataMember(Name = "value")]
    public string value;
    [DataMember(Name = "start_position")]
    public int startPosition;
    [DataMember(Name = "end_position")]
    public int endPosition;
    public bool isFinal = false;
  }

  public class Intent {
    public string intent;
    public bool isFinal = false;
  }

  public class MsgTranscript {
    public Word data;
  }

  public class MsgEntity {
    public Entity data;
  }

  public class MsgTentativeTranscript {
    public class Data {
      public string transcript;
      public Word[] words;
    }
    public Data data;
  }

  public class MsgTentativeEntity {
    public class Data {
      public Entity[] entities;

    }
    public Data data;
  }

  public class MsgIntent {
    public class Data {
      public string intent;

    }
    public Data data;
  }


  public class MsgCommon: IMsgCommonProps {
    public string type { get; set; }
    public string audio_context { get; set; }
    public int segment_id { get; set; }
  }

  public class Platform {
    public static string GetPersistentStoragePath()
    {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
      return UnityEngine.Application.streamingAssetsPath;
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
      return UnityEngine.Application.persistentDataPath;
#elif UNITY_ANDROID
      return UnityEngine.Application.persistentDataPath;
#elif UNITY_IOS
      return UnityEngine.Application.persistentDataPath;
#else
      return System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
#endif
    }

    // Parse string and return a valid Guid, attempting to retain the original string if it's a valid Guid.
    // return D-type Guid string: 00000000-0000-0000-0000-000000000000
    public static string GuidFromString(string s) {
      try {
        // Attempt to parse string as Guid as-is
        return new Guid(s).ToString();
      } catch {
        // Guid will be created from the string bytes
        var bytes = Encoding.UTF8.GetBytes(s);
        Array.Resize<byte>(ref bytes, 16);
        return new Guid(bytes).ToString();
      }
    }
  }

  [DataContract]
  public class SpeechlyConfig {
    public static string ConfigPath = "Speechly";
    public static string ConfigFileName = "SLUClientConfig.json";

    [DataMember(Name = "deviceId")]
    public string deviceId = null;

    public static SpeechlyConfig RestoreOrCreate() {
      string basePath = Platform.GetPersistentStoragePath();
      string pathToFile = Path.Combine(basePath, ConfigPath, ConfigFileName);
      SpeechlyConfig config = new SpeechlyConfig();
      try {
        string jsonString = File.ReadAllText(pathToFile, Encoding.UTF8);
        config = JSON.Parse(jsonString, config);
      } catch (Exception) {
        throw;
      }
      return config;
    }

    public void Save() {
      string basePath = Platform.GetPersistentStoragePath();
      Directory.CreateDirectory(Path.Combine(basePath, ConfigPath));

      string pathToFile = Path.Combine(basePath, ConfigPath, ConfigFileName);
      File.WriteAllText(pathToFile, JSON.Stringify(this), Encoding.UTF8);
    }
  }

}
