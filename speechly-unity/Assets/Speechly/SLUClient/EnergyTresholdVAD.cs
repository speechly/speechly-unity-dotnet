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
/// When enough loud frames have been detected, VAD activates and calls StartContext automatically. When enough silent frames have been detected, the VAD deactivates after the sustain time and StopContext is called automatically. The background noise energy gradually adapts when VAD is not active.
/// 
/// Use its public field to configure minimum energy level, signal-to-noise ratio, minimum activation time and an activation/deactivation treshold (ratio of loud to silent frames).
/// </summary>

  [System.Serializable]
   public class EnergyTresholdVAD {
    public bool Enabled = true;

    #if UNITY_EDITOR
    [Range(0.0f, 1.0f)]
    [Tooltip("Energy threshold - below this won't trigger activation")]
    #endif
    public float VADMinimumEnergy = 0.005f;

    #if UNITY_EDITOR
    [Range(1.0f, 10.0f)]
    [Tooltip("Signal-to-noise energy ratio needed for frame to be 'loud'")]
    #endif
    public float VADSignalToNoise = 2.0f;

    #if UNITY_EDITOR
    [Range(1, 32)]
    [Tooltip("Number of past frames analyzed for energy threshold VAD. Should be <= than HistoryFrames.")]
    #endif
    public int VADFrames = 5;

    #if UNITY_EDITOR
    [Range(.0f, 1.0f)]
    [Tooltip("Minimum 'loud' to 'silent' frame ratio in history to activate 'IsSignalDetected'")]
    #endif
    public float VADActivation = 0.7f;

    #if UNITY_EDITOR
    [Range(.0f, 1.0f)]
    [Tooltip("Maximum 'loud' to 'silent' frame ratio in history to inactivate 'IsSignalDetected'. Only evaluated when the sustain period is over.")]
    #endif
    public float VADRelease = 0.2f;

    #if UNITY_EDITOR
    [Range(0, 8000)]
    [Tooltip("Duration to keep 'IsSignalDetected' active. Renewed as long as VADActivation is holds true.")]
    #endif
    public int VADSustainMillis = 3000;

    #if UNITY_EDITOR
    [Range(0, 5000)]
    [Tooltip("Rate of background noise learn. Defined as duration in which background noise energy is moved halfway towards current frame's energy.")]
    #endif
    public int VADNoiseHalftimeMillis = 400;

    #if UNITY_EDITOR
    [Tooltip("Disable VAD listening control if you want to use the energy threshold but want to implement custom listening control by reading IsSignalDetected state.")]
    #endif
    public bool VADControlListening = true;

    public float Energy {get; private set; } = 0f;
    public float BaselineEnergy {get; private set; } = -1f;
    public bool IsSignalDetected {get; private set; }
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

      bool isLoudFrame = Energy > Math.Max(VADMinimumEnergy, BaselineEnergy * VADSignalToNoise);
      PushFrameHistory(isLoudFrame);

      IsSignalDetected = DetermineNewSignalState(IsSignalDetected);

      AdaptBackgroundNoise();
    }

    private bool DetermineNewSignalState(bool currentState) {
      vadSustainMillisLeft = Math.Max(vadSustainMillisLeft - FrameMillis, 0);

      int loudFrames = CountLoudFrames(VADFrames);
      float loudFrameRatio = (1f * loudFrames) / VADFrames;

      if (loudFrameRatio >= VADActivation) {
        vadSustainMillisLeft = VADSustainMillis;
        return true;
      }

      if (loudFrameRatio < VADRelease && vadSustainMillisLeft == 0) {
        return false;
      }

      return currentState;
    }

    private void AdaptBackgroundNoise() {
      // Gradually learn background noise level
      if (!IsSignalDetected) {
        if (VADNoiseHalftimeMillis > 0f) {
          var decay = (float)Math.Pow(2.0, -FrameMillis / (double)VADNoiseHalftimeMillis);
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