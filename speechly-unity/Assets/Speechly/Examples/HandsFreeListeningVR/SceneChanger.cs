using System;
﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace Speechly.Example.NoiseGateTriggerVR
{

public class SceneChanger : MonoBehaviour
{
    private bool button_pressed = false;
    private bool can_change_scene = false;

    void OnEnable()
    {
      can_change_scene = true;
    }

    public void ChangeScene()
    {
        if (can_change_scene) {
            int new_scene = SceneManager.GetActiveScene().buildIndex + 1;
            if (new_scene >= SceneManager.sceneCountInBuildSettings) {
                new_scene = 0;
            }
            Debug.Log($"Changing to scene {new_scene}");
            SceneManager.LoadScene(new_scene, LoadSceneMode.Single);
        }
    }

    // Update is called once per frame
    void Update()
    {
        List<InputDevice> inputDevices = new List<InputDevice>();
        InputDevices.GetDevices(inputDevices);

        bool some_button_pressed = false;
        foreach (InputDevice device in inputDevices) {

            if (device.TryGetFeatureValue(CommonUsages.primaryButton, out bool isPressed)) {
                if (isPressed) {
                    some_button_pressed = true;
                }
            }
        }
        if (button_pressed && !some_button_pressed) {
            ChangeScene();
        }
        button_pressed = some_button_pressed;
    }
}

}
