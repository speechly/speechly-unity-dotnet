using System;
using Speechly.Types;

#if UNITY_EDITOR
using UnityEngine;
#endif

namespace Speechly.Tools {
  [System.Serializable]

  public class AudioProcessor {
    public delegate void SendAudioDelegate(float[] floats, int start = 0, int length = -1);
    public delegate void VadChangeDelegate(bool isSignalDetected);

    public SendAudioDelegate OnSendAudio = (floats, start, length) => {};
    public VadChangeDelegate OnVadStateChange = (isSignalDetected) => {};
    public EnergyThresholdVAD Vad { get; private set; } = null;
    public AudioProcessorOptions Options { get; private set; }
    public AudioInfo Output {get; private set; }

    private bool isActive = false;
    private int frameSamples;
    private float[] sampleRingBuffer = null;
    private int frameSamplePos;
    private int currentFrameNumber = 0;
    private int streamFramePos = 0;
    private bool wasSignalDetected = false;
    private bool Debug;

    public AudioProcessor(
      AudioProcessorOptions options,
      AudioInfo output,
      bool debug = false
    ) {
      this.Options = options;
      this.Output = output;

      this.Vad = new EnergyThresholdVAD(this.Options.VADSettings, this.Output);
      this.Debug = debug;

      this.frameSamples = this.Options.InternalSampleRate * this.Options.FrameMillis / 1000;
      this.sampleRingBuffer = new float[this.frameSamples * this.Options.HistoryFrames];
    }

    public void Start() {
      isActive = true;
      Output.SamplesSent = 0;
      Output.UtteranceSerial++;
    }

    public void Stop() {
      Flush();
      isActive = false;
      wasSignalDetected = false;
    }

    public void Reset(int inputSampleRate = 0) {
      isActive = false;
      streamFramePos = 0;
      Output.StreamSamplePos = 0;
      frameSamplePos = 0;
      currentFrameNumber = 0;
      Output.UtteranceSerial = -1;
      if (inputSampleRate > 0) this.Options.InputSampleRate = inputSampleRate;
      Vad?.ResetVAD();
    }

    private void Flush() {
      ProcessAudio(sampleRingBuffer, 0, frameSamplePos, true);
    }


/// <summary>
/// Process speech audio samples from a microphone or other audio source.
///
/// It's recommended to constantly feed new audio as long as you want to use Speechly's SLU services.
///
/// You can control when to start and stop process speech either manually with <see cref="Start"/> and <see cref="Stop"/> or
/// automatically by providing a voice activity detection (VAD) field to <see cref="SpeechlyClient"/>.
/// 
/// The audio is handled as follows:
/// - Downsample to 16kHz if needed
/// - Add to history ringbuffer
/// - Calculate energy (VAD)
/// - Automatic Start/Stop (VAD)
/// - Send utterance audio to a file
/// - Send utterance audio to Speechly SLU decoder
/// </summary>
/// <param name="floats">Array of float containing samples to feed to the audio pipeline. Each sample needs to be in range -1f..1f.</param>
/// <param name="start">Start index of audio to process in samples (default: `0`).</param>
/// <param name="length">Length of audio to process in samples or `-1` to process the whole array (default: `-1`).</param>
/// <param name="forceSubFrameProcess">Forces processing of last subframe at end of audio stream (default: `false`).</param>

    public void ProcessAudio(float[] floats, int start = 0, int length = -1, bool forceSubFrameProcess = false) {
      if (length < 0) length = floats.Length;
      if (length == 0) return;

      int i = start;
      int endIndex = start + length;

      while (i < endIndex) {
        int frameBase = currentFrameNumber * frameSamples;

        if (Options.InputSampleRate == Options.InternalSampleRate) {
          // Copy input samples to fill current ringbuffer frame
          int samplesToFillFrame = Math.Min(endIndex - i, frameSamples - frameSamplePos);
          int frameEndIndex = frameSamplePos + samplesToFillFrame;
          while (frameSamplePos < frameEndIndex) {
            sampleRingBuffer[frameBase + frameSamplePos++] = floats[i++];
          }
        } else {
          // Downsample input samples to fill current ringbuffer frame
          float ratio = 1f * Options.InputSampleRate / Options.InternalSampleRate;
          int inputSamplesToFillFrame = Math.Min(endIndex - i, (int)Math.Round(ratio * (frameSamples - frameSamplePos)));
          int samplesToFillFrame = Math.Min((int)Math.Round((endIndex - i) / ratio), frameSamples - frameSamplePos);
          AudioTools.Downsample(floats, ref sampleRingBuffer, i,inputSamplesToFillFrame, frameBase+frameSamplePos,samplesToFillFrame);
          i += inputSamplesToFillFrame;
          frameSamplePos += samplesToFillFrame;
        }

        // Process frame
        if (frameSamplePos == frameSamples || forceSubFrameProcess) {
          frameSamplePos = 0;
          int subFrameSamples = forceSubFrameProcess ? frameSamplePos : frameSamples;

          if (!forceSubFrameProcess) {
            ProcessFrame(sampleRingBuffer, frameBase, subFrameSamples);
          }

          if (isActive) {
            
            if (Output.SamplesSent == 0) {
              // Start of the utterance - send history frames
              int sendHistory = Math.Min(streamFramePos, Options.HistoryFrames - 1);
              int historyFrameIndex = (currentFrameNumber + Options.HistoryFrames - sendHistory) % Options.HistoryFrames;
              while (historyFrameIndex != currentFrameNumber) {
                OnSendAudio(sampleRingBuffer, historyFrameIndex * frameSamples, frameSamples);
                Output.SamplesSent += frameSamples;
                historyFrameIndex = (historyFrameIndex + 1) % Options.HistoryFrames;
              }
            }
            OnSendAudio(sampleRingBuffer, frameBase, subFrameSamples);
            Output.SamplesSent += subFrameSamples;
          }

          streamFramePos += 1;
          Output.StreamSamplePos += subFrameSamples;
          currentFrameNumber = (currentFrameNumber + 1) % Options.HistoryFrames;
        }
      }
    }

    private void ProcessFrame(float[] floats, int start = 0, int length = -1) {
      AnalyzeAudioFrame(in floats, start, length);
      AutoControlListening();
    }

    private void AnalyzeAudioFrame(in float[] waveData, int s, int frameSamples) {
      if (this.Vad != null && this.Vad.Options.Enabled) {
        Vad.ProcessFrame(waveData, s, frameSamples);
      }
    }

    private void AutoControlListening() {
      if (this.Vad != null && this.Vad.Options.Enabled && this.Options.VADControlsListening) {
        if (Output.IsSignalDetected != this.wasSignalDetected) {
          OnVadStateChange(Output.IsSignalDetected);
          this.wasSignalDetected = Output.IsSignalDetected;
        }
      }
    }
  }
}
