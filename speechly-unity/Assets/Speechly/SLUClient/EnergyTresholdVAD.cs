using System;
using Speechly.Tools;

#if UNITY_EDITOR
using UnityEngine;
#endif

namespace Speechly.SLUClient {
/// <summary>
/// Adaptive energy threshold voice activity detection (VAD) implementation.
/// It can be used to enable hands-free operation of the SLU decoder.
///
/// When enough frames with a signal stronger than SignalToNoiseDb have been detected, IsSignalDetected goes true. When enough silent frames have been detected, IsSignalDetected goes false after the sustain time.
/// Use its public fields to configure the static noise gate level, signal-to-noise level, activation/deactivation treshold (ratio of signal to silent frames) and the signal sustain time.
/// The background noise level gradually adapts when no signal is detected.
///
/// IsSignalDetected can be used to drive SpeechlyClient's StartContext and StopContext automatically by setting ControlListening true.
/// </summary>

  [System.Serializable]
   public class EnergyTresholdVAD {
    #if UNITY_EDITOR
    [Tooltip("Run energy analysis.")]
    #endif
    public bool Enabled = true;

    #if UNITY_EDITOR
    [Range(0.0f, 10.0f)]
    [Tooltip("Signal-to-noise energy ratio needed for frame to be 'loud'")]
    #endif
    public float SignalToNoiseDb = 3.0f;

    #if UNITY_EDITOR
    [Range(-90.0f, 0.0f)]
    [Tooltip("Energy threshold - below this won't trigger activation")]
    #endif
    public float NoiseGateDb = -24f;

    #if UNITY_EDITOR
    [Range(0, 5000)]
    [Tooltip("Rate of background noise learn. Defined as duration in which background noise energy is moved halfway towards current frame's energy.")]
    #endif
    public int NoiseLearnHalftimeMillis = 400;

    #if UNITY_EDITOR
    [Range(1, 32)]
    [Tooltip("Number of past frames analyzed for energy threshold VAD. Should be <= than HistoryFrames.")]
    #endif
    public int SignalSearchFrames = 5;

    #if UNITY_EDITOR
    [Range(.0f, 1.0f)]
    [Tooltip("Minimum 'signal' to 'silent' frame ratio in history to activate 'IsSignalDetected'")]
    #endif
    public float SignalActivation = 0.7f;

    #if UNITY_EDITOR
    [Range(.0f, 1.0f)]
    [Tooltip("Maximum 'signal' to 'silent' frame ratio in history to inactivate 'IsSignalDetected'. Only evaluated when the sustain period is over.")]
    #endif
    public float SignalRelease = 0.2f;

    #if UNITY_EDITOR
    [Range(0, 8000)]
    [Tooltip("Duration to keep 'IsSignalDetected' active. Renewed as long as VADActivation is holds true.")]
    #endif
    public int SignalSustainMillis = 3000;

    #if UNITY_EDITOR
    [Header("Output")]
    #endif
    public bool IsSignalDetected;

    #if UNITY_EDITOR
    [Range(0.0f, 10.0f)]
    #endif
    public float SignalDb;

    #if UNITY_EDITOR
    [Range(-90.0f, 0.0f)]
    #endif
    public float NoiseLevelDb;

    #if UNITY_EDITOR
    [Header("Signal detection controls listening")]
    [Tooltip("Enable listening control if you want to use IsSignalDetected to control SpeechlyClient's StartContext/StopContext.")]
    #endif
    public bool ControlListening = true;


    public float Energy {get; private set; } = 0f;
    public float BaselineEnergy {get; private set; } = -1f;
    private int loudFrameBits = 0;
    private float vadSustainMillisLeft = 0;
    private int FrameMillis = 30;

    public void ProcessFrame(float[] floats, int start = 0, int length = -1) {
      if (!Enabled) {
        ResetVAD();
        return;
      }

      Energy = AudioTools.GetEnergy(in floats, start, length);

      if (BaselineEnergy < 0f) {
        BaselineEnergy = Energy;
      }

      bool isLoudFrame = Energy > Math.Max(Math.Pow(10.0, NoiseGateDb / 10.0), BaselineEnergy * Math.Pow(10.0, SignalToNoiseDb / 10.0));
      PushFrameHistory(isLoudFrame);

      IsSignalDetected = DetermineNewSignalState(IsSignalDetected);

      AdaptBackgroundNoise();

      SignalDb = AudioTools.EnergyToDb(Energy / BaselineEnergy);
      NoiseLevelDb = AudioTools.EnergyToDb(BaselineEnergy);
    }

    private bool DetermineNewSignalState(bool currentState) {
      vadSustainMillisLeft = Math.Max(vadSustainMillisLeft - FrameMillis, 0);

      int loudFrames = CountLoudFrames(SignalSearchFrames);

      int activationFrames = (int)Math.Round(SignalActivation * SignalSearchFrames);
      int releaseFrames = (int)Math.Round(SignalRelease * SignalSearchFrames);

      if (loudFrames >= activationFrames) {
        // Renew sustain time
        vadSustainMillisLeft = SignalSustainMillis;
        return true;
      }
      
      if (loudFrames <= releaseFrames && vadSustainMillisLeft == 0) {
        return false;
      }

      return currentState;
    }

    private void AdaptBackgroundNoise() {
      // Gradually learn background noise level
      if (!IsSignalDetected) {
        if (NoiseLearnHalftimeMillis > 0f) {
          var decay = (float)Math.Pow(2.0, -FrameMillis / (double)NoiseLearnHalftimeMillis);
          BaselineEnergy = (BaselineEnergy * decay) + (Energy * (1f - decay));
        }
      }
    }

    private void PushFrameHistory(bool isLoud) {
      loudFrameBits = (isLoud ? 1 : 0) | (loudFrameBits << 1);
    }

    private int CountLoudFrames(int numHistoryFrames) {
      int numActiveFrames = 0;
      int t = loudFrameBits;
      while (numHistoryFrames > 0) {
        if ((t & 1) == 1) numActiveFrames++;
        t = t >> 1;
        numHistoryFrames--;
      }
      return numActiveFrames;
    }

    private void ResetVAD() {
      IsSignalDetected = false;
      loudFrameBits = 0;
      Energy = 0f;
      BaselineEnergy = -1f;
    }
  }

}