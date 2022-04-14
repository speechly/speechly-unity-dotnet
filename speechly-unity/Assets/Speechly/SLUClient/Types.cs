using System;
using System.Runtime.Serialization;
using System.Text;
using System.IO;

namespace Speechly.Types {

  public delegate void SegmentChangeDelegate(Speechly.SLUClient.Segment segment);
  public delegate void TentativeTranscriptDelegate(MsgTentativeTranscript msg);
  public delegate void TranscriptDelegate(MsgTranscript msg);
  public delegate void TentativeEntityDelegate(MsgTentativeEntity msg);
  public delegate void EntityDelegate(MsgEntity msg);
  public delegate void IntentDelegate(MsgIntent msg);
  public delegate void StartStreamDelegate();
  public delegate void StopStreamDelegate();
  public delegate void StartContextDelegate();
  public delegate void StopContextDelegate();

  public delegate string BeautifyIntent (string intent);
  public delegate string BeautifyEntity (string entityValue, string entityType);

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


  public class MsgCommon {
    public string type { get; set; }
    public string audio_context { get; set; }
    public int segment_id { get; set; }
  }

  [DataContract]
  internal class Preferences {
    public static string FileName = "SLUClientConfig.json";

    [DataMember(Name = "deviceId")]
    public string deviceId = null;
  }

}
