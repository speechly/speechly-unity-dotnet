using System;
using Speechly.Types;

#if UNITY_EDITOR
using UnityEngine;
#endif

namespace Speechly.Tools {
/// <summary>
/// Adaptive energy threshold voice activity detection (VAD) implementation.
/// It can be used to enable hands-free operation of the SLU decoder.
///
/// When enough frames with a signal stronger than SignalToNoiseDb have been detected, IsSignalDetected goes true. When enough silent frames have been detected, IsSignalDetected goes false after the sustain time.
/// Use its public fields to configure the static noise gate level, signal-to-noise level, activation/deactivation treshold (ratio of signal to silent frames) and the signal sustain time.
/// The background noise level gradually adapts when no signal is detected.
///
/// IsSignalDetected can be used to drive SpeechlyClient's Start and Stop automatically by setting ControlListening true.
/// </summary>
  [System.Serializable]

   public class EnergyThresholdVAD {
    public VADOptions Options {get; private set; }
    public AudioInfo Output {get; private set; }

    private float energy = 0f;
    private float baselineEnergy = -1f;
    private int loudFrameBits = 0;
    private float vadSustainMillisLeft = 0;
    private int frameMillis = 30;

    public EnergyThresholdVAD(VADOptions options, AudioInfo output) {
      this.Output = output;
      this.Options = options;
    }

    public void ProcessFrame(float[] floats, int start = 0, int length = -1) {
      if (!Options.Enabled) {
        ResetVAD();
        return;
      }

      energy = AudioTools.GetEnergy(in floats, start, length);

      if (baselineEnergy < 0f) {
        baselineEnergy = energy;
      }

      bool isLoudFrame = energy > Math.Max(Math.Pow(10.0, Options.NoiseGateDb / 10.0), baselineEnergy * Math.Pow(10.0, Options.SignalToNoiseDb / 10.0));
      PushFrameHistory(isLoudFrame);

      Output.IsSignalDetected = DetermineNewSignalState(Output.IsSignalDetected);

      AdaptBackgroundNoise();

      Output.SignalDb = AudioTools.EnergyToDb(energy / baselineEnergy);
      Output.NoiseLevelDb = AudioTools.EnergyToDb(baselineEnergy);
    }

    private bool DetermineNewSignalState(bool currentState) {
      vadSustainMillisLeft = Math.Max(vadSustainMillisLeft - frameMillis, 0);

      int loudFrames = CountLoudFrames(Options.SignalSearchFrames);

      int activationFrames = (int)Math.Round(Options.SignalActivation * Options.SignalSearchFrames);
      int releaseFrames = (int)Math.Round(Options.SignalRelease * Options.SignalSearchFrames);

      if (loudFrames >= activationFrames) {
        // Renew sustain time
        vadSustainMillisLeft = Options.SignalSustainMillis;
        return true;
      }
      
      if (loudFrames <= releaseFrames && vadSustainMillisLeft == 0) {
        return false;
      }

      return currentState;
    }

    private void AdaptBackgroundNoise() {
      // Gradually learn background noise level
      if (!Output.IsSignalDetected) {
        if (Options.NoiseLearnHalftimeMillis > 0f) {
          var decay = (float)Math.Pow(2.0, -frameMillis / (double)Options.NoiseLearnHalftimeMillis);
          baselineEnergy = (baselineEnergy * decay) + (energy * (1f - decay));
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

    public void ResetVAD() {
      Output.IsSignalDetected = false;
      loudFrameBits = 0;
      energy = 0f;
      baselineEnergy = -1f;
    }
  }

}