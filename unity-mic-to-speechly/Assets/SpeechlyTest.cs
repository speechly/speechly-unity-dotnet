using System.Collections;
using System.Collections.Generic;
using UnityEngine;

using Speechly.SLUClient;
using Logger = Speechly.SLUClient.Logger;

public class SpeechlyTest : MonoBehaviour
{
    async void Start()
    {
        Logger.Log = Debug.Log;
        await SpeechlyClientTest.test();
    }
}
