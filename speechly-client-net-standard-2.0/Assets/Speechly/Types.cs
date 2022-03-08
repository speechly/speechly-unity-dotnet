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

  [DataContract]
  public class SpeechlyConfig {
    public static string ConfigPath = "Speechly";
    public static string ConfigFileName = "SLUClientConfig.json";

    [DataMember(Name = "deviceId")]
    public string deviceId = null;

    public static SpeechlyConfig RestoreOrCreate() {
      string basePath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
      string pathToFile = Path.Combine(basePath, ConfigPath, ConfigFileName);
      SpeechlyConfig config = new SpeechlyConfig();
      try {
        string jsonString = File.ReadAllText(pathToFile, Encoding.UTF8);
        config = JSON.Parse(jsonString, config);
      } catch (Exception) {
      }
      return config;
    }

    public void Save() {
      string basePath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
      Directory.CreateDirectory(Path.Combine(basePath, ConfigPath));

      string pathToFile = Path.Combine(basePath, ConfigPath, ConfigFileName);
      File.WriteAllText(pathToFile, JSON.Stringify(this), Encoding.UTF8);
    }
  }

}
