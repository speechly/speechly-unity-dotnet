using UnityEngine;

using Speechly.SLUClient;
using Logger = Speechly.SLUClient.Logger;

public class SpeechlyTest : MonoBehaviour
{
  async void Start()
  {
    Logger.Log = Debug.Log;
    await SpeechlyClientTest.test("Assets/SpeechlyExamples/AudioFileToSpeechly/SLUClientTest/00_chinese_restaurant.raw");
  }
}
