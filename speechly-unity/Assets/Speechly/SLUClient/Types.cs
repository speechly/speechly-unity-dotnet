using System;
using System.Runtime.Serialization;
using System.Text;
using System.IO;

namespace Speechly.SLUClient {

  public struct SegmentMessage {
    public MsgCommon msgCommon;
    public string msgString;
    public SegmentMessage(MsgCommon msgCommon, string msgString) {
      this.msgCommon = msgCommon;
      this.msgString = msgString;
    }
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

  public class ConfigTool {
    public static string ConfigPath = "Speechly";

    [DataMember(Name = "deviceId")]
    public string deviceId = null;

    public static T RestoreOrCreate<T>(string ConfigFileName) where T: class, new() {
      string pathToFile = Path.Combine(Platform.GetPersistentStoragePath(), ConfigPath, ConfigFileName);
      T config = new T();
      try {
        string jsonString = File.ReadAllText(pathToFile, Encoding.UTF8);
        config = JSON.Parse(jsonString, config);
      } catch (Exception) {
      }
      return config;
    }

    public static void Save<T>(T configData, string ConfigFileName) {
      Directory.CreateDirectory(Path.Combine(Platform.GetPersistentStoragePath(), ConfigPath));

      string pathToFile = Path.Combine(Platform.GetPersistentStoragePath(), ConfigPath, ConfigFileName);
      File.WriteAllText(pathToFile, JSON.Stringify(configData), Encoding.UTF8);
    }
  }

  [DataContract]
  public class Preferences {
    public static string FileName = "SLUClientConfig.json";

    [DataMember(Name = "deviceId")]
    public string deviceId = null;
  }

}
