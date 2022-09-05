using System;
using System.Runtime.Serialization;
using System.Collections.Generic;
using System.Text;
using System.IO;

#if UNITY_EDITOR
using UnityEngine;
#endif

namespace Speechly.Types {

  public delegate void SegmentChangeDelegate(Speechly.SLUClient.Segment segment);
  public delegate void TentativeTranscriptDelegate(MsgTentativeTranscript msg);
  public delegate void TranscriptDelegate(MsgTranscript msg);
  public delegate void TentativeEntityDelegate(MsgTentativeEntity msg);
  public delegate void EntityDelegate(MsgEntity msg);
  public delegate void IntentDelegate(MsgIntent msg);
  public delegate void StartStreamDelegate();
  public delegate void StopStreamDelegate();
  public delegate void StartDelegate();
  public delegate void StopDelegate();

  public delegate string BeautifyIntent (string intent);
  public delegate string BeautifyEntity (string entityValue, string entityType);


  public class ModelExpiredException : Exception
  {
    public ModelExpiredException() {}
    public ModelExpiredException(string message) : base(message) {}
    public ModelExpiredException(string message, Exception inner) : base(message, inner) {}
  }

  /// <summary>
  /// Statistics calculated by the Audio Processor. Note that this information is not currently available when using libSpeechly AudioProcessor.
  /// </summary>
  [System.Serializable]
  public class AudioInfo {
    /// <value>
    /// True if VAD has detected a loud signal.
    /// </value>
    public bool IsSignalDetected;

    /// <value>
    /// Signal level in dB above noise level.
    /// </value>
    #if UNITY_EDITOR
    [Range(0.0f, 10.0f)]
    #endif
    public float SignalDb;

    /// <value>
    /// Current noise level in dB.
    /// </value>
    #if UNITY_EDITOR
    [Range(-90.0f, 0.0f)]
    #endif
    public float NoiseLevelDb;

    /// <value>
    /// Current count of continuously processed samples (thru ProcessAudio) since StartStream
    /// </value>
    public int StreamSamplePos { get; internal set; } = 0;

    /// <value>
    /// Current count of processed samples since Start
    /// </value>
    public int SamplesSent { get; internal set; } = 0;

    /// <value>
    /// 0-based local index of utterance within the stream. -1 if no utterances have been processed.
    /// </value>
    public int UtteranceSerial { get; internal set; } = -1;
  }

  /// <summary>
  /// Options for Audio Processor and Voice Activity Detection (VAD)
  /// </summary>
  [System.Serializable]
  public class AudioProcessorOptions {
    /// <value>
    /// Input sample rate in Hz.
    /// </value>
    public int InputSampleRate = 16000;

    /// <value>
    /// Internal Speechly sample rate in Hz. Currently only 16000 Hz is supported.
    /// </value>
    public int InternalSampleRate = 16000;

    /// <value>
    /// Internal frame length. Affects audio caching and VAD energy analysis.
    /// </value>
    public int FrameMillis = 30;

    /// <value>
    /// Total number of history frames to keep in memory. Will be sent upon a starting a new utterance.
    /// </value>
    #if UNITY_EDITOR
    [Range(1, 32)]
    [Tooltip("Number of frames to keep in memory. When listening is started, history frames are sent to capture the lead-in audio.")]
    #endif
    public int HistoryFrames = 8;

    /// <value>
    /// Allows VAD's IsSignalDetected to control SpeechlyClient's Start/Stop.
    /// </value>
    #if UNITY_EDITOR
    [Tooltip("Allows VAD's IsSignalDetected to control SpeechlyClient's Start/Stop.")]
    #endif
    public bool VADControlsListening = false;

    #if UNITY_EDITOR
    [SerializeField]
    #endif
    public VADOptions VADSettings = new VADOptions();
  }

  /// <summary>
  /// Options for Voice Activity Detection (VAD)
  /// </summary>
  [System.Serializable]
  public class VADOptions {
    /// <value>
    /// Enable energy-level calculations
    /// </value>
    public bool Enabled { get; set; } = true;

    /// <value>
    /// Signal-to-noise energy ratio needed for frame to be 'loud'
    /// </value>
    #if UNITY_EDITOR
    [Range(0.0f, 10.0f)]
    [Tooltip("Signal-to-noise energy ratio needed for frame to be 'loud'")]
    #endif
    public float SignalToNoiseDb = 3.0f;  /// Signal-to-noise energy ratio needed for frame to be 'loud'

    /// <value>
    /// Energy threshold - below this won't trigger activation
    /// </value>
    #if UNITY_EDITOR
    [Range(-90.0f, 0.0f)]
    [Tooltip("Energy threshold - below this won't trigger activation")]
    #endif
    public float NoiseGateDb = -24f;

    /// <value>
    /// Rate of background noise learn. Defined as duration in which background noise energy is moved halfway towards current frame's energy.
    /// </value>
    #if UNITY_EDITOR
    [Range(0, 5000)]
    [Tooltip("Rate of background noise learn. Defined as duration in which background noise energy is moved halfway towards current frame's energy.")]
    #endif
    public int NoiseLearnHalftimeMillis = 400;

    /// <value>
    /// Number of past frames analyzed for energy threshold VAD. Should be <= than HistoryFrames.
    /// </value>
    #if UNITY_EDITOR
    [Range(1, 32)]
    [Tooltip("Number of past frames analyzed for energy threshold VAD. Should be <= than HistoryFrames.")]
    #endif
    public int SignalSearchFrames = 5;

    /// <value>
    /// Minimum 'signal' to 'silent' frame ratio in history to activate 'IsSignalDetected'
    /// </value>
    #if UNITY_EDITOR
    [Range(.0f, 1.0f)]
    [Tooltip("Minimum 'signal' to 'silent' frame ratio in history to activate 'IsSignalDetected'")]
    #endif
    public float SignalActivation = 0.7f;

    /// <value>
    /// Maximum 'signal' to 'silent' frame ratio in history to inactivate 'IsSignalDetected'. Only evaluated when the sustain period is over.
    /// </value>
    #if UNITY_EDITOR
    [Range(.0f, 1.0f)]
    [Tooltip("Maximum 'signal' to 'silent' frame ratio in history to inactivate 'IsSignalDetected'. Only evaluated when the sustain period is over.")]
    #endif
    public float SignalRelease = 0.2f;

    /// <value>
    /// Duration to keep 'IsSignalDetected' active. Renewed as long as VADActivation is holds true.
    /// </value>
    #if UNITY_EDITOR
    [Range(0, 8000)]
    [Tooltip("Duration to keep 'IsSignalDetected' active. Renewed as long as VADActivation is holds true.")]
    #endif
    public int SignalSustainMillis = 3000;
  }

  /// <summary>
  /// Options for speech recognition
  /// </summary>
  [System.Serializable]
  public class ContextOptions {
    /// <summary>
    /// Increase or decrease the probability of recognizing the provided words
    /// </summary>
    [System.Serializable]
    public class ShallowFusionSettings {
      /// <value>
      /// Newline-delimited words that specify the vocabulary for score biasing.
      /// </value>
      #if UNITY_EDITOR
      [TextArea]
      [Tooltip("Newline-delimited words that specify the vocabulary for score biasing.")]
      #endif
      public string Vocabulary;

      /// <value>
      /// Biasing weight. Positive values increase the probability of recognition, negative values decrease it.
      /// </value>
      #if UNITY_EDITOR
      [Range(-4.0f, 4.0f)]
      [Tooltip("Biasing weight. Positive values increase the probability of recognition, negative values decrease it.")]
      #endif
      public float Weight;
    }

    #if UNITY_EDITOR
    [Range(0, 1200)]
    [Tooltip("Duration of silence in milliseconds that creates a new segment. Set to 0 to disable speech segmentation.")]
    [SerializeField]
    #endif
    public int SilenceSegmentationMillis = 720;

    #if UNITY_EDITOR
    [SerializeField]
    #endif
    public ShallowFusionSettings BoostVocabulary;
  }

  [DataContract]
  public class StartMessage {
    [DataContract]
    public class Options {
      [DataMember(Name = "vocabulary", EmitDefaultValue=false)]
      public string[] vocabulary = null;
      [DataMember(Name = "vocabulary_bias", EmitDefaultValue=false)]
      public string[] vocabulary_bias = null;
      [DataMember(Name = "silence_triggered_segmentation", EmitDefaultValue=false)]
      public string[] silence_triggered_segmentation;
    }
    [DataMember(Name = "event")]
    public string eventType;
    [DataMember(Name = "appId", EmitDefaultValue=false)]
    public string appId;
    [DataMember(Name = "options", EmitDefaultValue=false)]
    public Options options;
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
