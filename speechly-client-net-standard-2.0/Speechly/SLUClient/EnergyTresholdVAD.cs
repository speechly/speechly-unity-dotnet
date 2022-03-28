using System;

namespace Speechly.SLUClient {
  public class EnergyTresholdVAD {
    // [Range(0.0f, 1.0f)]
    // [Tooltip("Energy threshold - below this won't trigger activation")]
    public float VADMinimumEnergy = 0.005f;
    // [Range(1.0f, 10.0f)]
    // [Tooltip("Signal-to-noise energy ratio needed for frame to be 'loud'")]
    public float VADSignalToNoise = 2.0f;
    // [Range(1, 32)]
    // [Tooltip("Number of past frames analyzed for energy threshold VAD. Should be <= than HistoryFrames.")]
    public int VADFrames = 5;
    // [Range(.0f, 1.0f)]
    // [Tooltip("Minimum 'loud' to 'silent' frame ratio in history to activate 'IsSignalDetected'")]
    public float VADActivation = 0.7f;
    // [Range(.0f, 1.0f)]
    // [Tooltip("Maximum 'loud' to 'silent' frame ratio in history to inactivate 'IsSignalDetected'. Only evaluated when the sustain period is over.")]
    public float VADRelease = 0.2f;
    // [Range(0, 8000)]
    // [Tooltip("Duration to keep 'IsSignalDetected' active. Renewed as long as VADActivation is holds true.")]
    public int VADSustainMillis = 3000;
    // [Range(0, 5000)]
    // [Tooltip("Rate of background noise learn. Defined as duration in which background noise energy is moved halfway towards current frame's energy.")]
    public int VADNoiseHalftimeMillis = 400;
    // [Tooltip("Disable VAD listening control if you want to use the energy threshold but want to implement custom listening control by reading IsSignalDetected state.")]
    public bool VADControlListening = true;
    public float Energy {get; private set; } = 0f;
    public float BaselineEnergy {get; private set; } = -1f;
    public bool IsSignalDetected {get; private set; }
    private int loudFrameBits = 0;
    private float vadSustainMillisLeft = 0;
    private int FrameMillis = 30;

    public void ProcessFrame(float[] floats, int start = 0, int length = -1) {
      Energy = AudioTools.GetEnergy(in floats, start, length);

      if (BaselineEnergy < 0f) {
        BaselineEnergy = Energy;
      }

      bool isLoudFrame = Energy > Math.Max(VADMinimumEnergy, BaselineEnergy * VADSignalToNoise);
      PushFrameHistory(isLoudFrame);
      IsSignalDetected = DetermineNewSignalState(IsSignalDetected);
      Logger.Log($"{IsSignalDetected} {Energy.ToString()}");
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
  }

}