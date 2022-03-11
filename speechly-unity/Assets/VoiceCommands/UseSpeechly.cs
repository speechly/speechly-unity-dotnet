using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Speechly.SLUClient;
using UnityEngine.UI;
using TMPro;
using System.Linq;

namespace Speechly.Demo.VoiceCommands
{

  public class UseSpeechly : MonoBehaviour
  {
    public Camera Cam;
    public GameObject Cube;
    public GameObject Sphere;
    public Slider SliderAudioPeak;
    public TMP_Text TranscriptText;
    private SpeechlyClient speechlyClient;
    private bool IsButtonHeld = false;

    void Start()
    {
      speechlyClient = MicToSpeechly.Instance.SpeechlyClient;
      speechlyClient.OnSegmentChange += (segment) =>
      {
        Debug.Log(segment.ToString());
        TranscriptText.text = segment.ToString(
          (intent) => "",
          (words, entityType) => $"<b>{words.ToUpper()}</b>",
          ""
        );

        if (selection != null) {
          var size = segment.entities.Select(entry => entry.Value).ToList().Where(entity => entity.type == "size").Select(entity => entity.value).ToArray().FirstOrDefault();
          var color = segment.entities.Select(entry => entry.Value).ToList().Where(entity => entity.type == "color").Select(entity => entity.value).ToArray().FirstOrDefault();
          var shape = segment.entities.Select(entry => entry.Value).ToList().Where(entity => entity.type == "shape").Select(entity => entity.value).ToArray().FirstOrDefault();

          //.Where(entity => entity.type == "size").Select(entity => entity.value).First();
          if (size == "big") selection.localScale = new Vector3(2,2,2);
          if (size == "medium") selection.localScale = new Vector3(1,1,1);
          if (size == "small") selection.localScale = new Vector3(0.5f,0.5f,0.5f);
          if (size == "tall") selection.localScale = new Vector3(0.5f,10f,0.5f);

          var mesh = selection.GetComponent<MeshFilter>();
          if (color == "yellow") mesh.GetComponent<Renderer>().material.color = new Color(1,1,0,1);
          if (color == "red") mesh.GetComponent<Renderer>().material.color = new Color(1,0.2f,0.2f,1);
          if (color == "pink") mesh.GetComponent<Renderer>().material.color = new Color(1,0.8f,0.8f,1);
          if (color == "blue") mesh.GetComponent<Renderer>().material.color = new Color(0.2f,0.3f,1,1);
          if (color == "green") mesh.GetComponent<Renderer>().material.color = new Color(0.2f,1f,0.1f,1);

          if (shape == "sphere") mesh.mesh = Sphere.GetComponent<MeshFilter>().sharedMesh;
          if (shape == "cube") mesh.mesh = Cube.GetComponent<MeshFilter>().sharedMesh;

          // Set visibility
          selection.gameObject.SetActive(segment.intent.intent != "delete");
        }
      };
    }

    private Transform selection;

    async void Update()
    {
      SliderAudioPeak.value = MicToSpeechly.Instance.Peak;

      if (Input.GetMouseButtonDown(0)) {
        Debug.Log("Button press");
        TranscriptText.text = "LISTENING...";

        if (!speechlyClient.IsListening) {
          await speechlyClient.StartContext();
        }

        RaycastHit hit;
        Ray ray = Cam.ScreenPointToRay(Input.mousePosition);
        
        if (Physics.Raycast(ray, out hit)) {
          selection = hit.transform;
          selection.gameObject.GetComponent<Outline>().enabled = true;
          Debug.Log(selection);
        } else {
          selection = null;
        }
      }

      if (Input.GetMouseButtonUp(0)) {
        Debug.Log("Button release");
        if (TranscriptText.text == "LISTENING...") {
          TranscriptText.text = "Point-and-Talk Demo";
        }
        if (selection != null) {
          selection.gameObject.GetComponent<Outline>().enabled = false;
        }
        if (speechlyClient.IsListening) {
          await speechlyClient.StopContext();
        }
      }
    }

    public async void OnMouseDown()
    {
      if (!IsButtonHeld && !speechlyClient.IsListening)
      {
        await speechlyClient.StartContext();
        IsButtonHeld = true;
        Debug.Log("Mouse Down");
      }
    }

    public async void OnMouseUp()
    {
      if (IsButtonHeld && speechlyClient.IsListening)
      {
        Debug.Log("Mouse Up");
        IsButtonHeld = false;
        await speechlyClient.StopContext();
      }
    }

  }
}