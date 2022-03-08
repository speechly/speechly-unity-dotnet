using System;
using System.Text;
using System.Collections;
using System.Linq;
using System.Collections.Generic;

namespace Speechly.SLUClient {

  public class Segment {
    int id;
    string contextId;
    bool isFinal = false;
    Word[] words = new Word[0];
    Dictionary<string, Entity> entities = new Dictionary<string, Entity>();
    Intent intent = new Intent{ intent = "", isFinal = false };

    public Segment(string audio_context, int segment_id) {
      this.contextId = audio_context;
      this.id = segment_id;
    }

    public override string ToString() {
      var sb = new StringBuilder();
      var entityIds = new string[words.Length];
      foreach (KeyValuePair<string, Entity> entity in entities) {
        for (var i = entity.Value.startPosition; i < entity.Value.endPosition; i++) {
          entityIds[i] = entity.Key;
        }
      }

      sb.Append($"*{this.intent.intent} ");
      sb.Append(String.Join(" ", this.words.Select((i, index) => {
        if (i == null) return null;
        if (entityIds[index] != null) {
          var entity = entities[entityIds[index]];
          if (entity.startPosition == index && entity.endPosition == index + 1) {
            return $"[{i.word}]({entity.type})";
          } else if (entity.startPosition == index) {
            return $"[{i.word}";
          } else {
            return $"{i.word}]({entity.type})";
          }
        }
        return i.word;
      }).Where(i => i != null).ToArray()));

      if (this.isFinal) sb.Append(" ✔");

      return sb.ToString();
    }

    public void UpdateTranscript(Word word) {
      if (word.index >= words.Length) {
        Array.Resize(ref words, word.index + 1);
      }
      words[word.index] = word;
    }

    public void UpdateTranscript(Word[] words) {
      foreach(Word w in words) UpdateTranscript(w);
    }

    public void UpdateEntity(Entity entity) {
      entities[EntityMapKey(entity)] = entity;
    }

    public void UpdateEntity(Entity[] entities) {
      foreach(Entity entity in entities) UpdateEntity(entity);
    }

    public void UpdateIntent(string intent, bool isFinal) {
      this.intent.intent = intent;
      this.intent.isFinal = isFinal;
    }

    public void EndSegment() {
      // Filter away any entities which were not finalized.
      foreach (KeyValuePair<string, Entity> entity in entities) {
        if (!entity.Value.isFinal) {
          this.entities.Remove(entity.Key);
        }
      }

      // Filter away any transcripts which were not finalized. Keep indices intact.
      for (int i = 0; i < words.Length; i++) {
        if (words[i] != null) {
          if (!words[i].isFinal) {
            words[i] = null;
          }
        }
      }

      if (!this.intent.isFinal) {
        this.intent.intent = "";
        this.intent.isFinal = true;
      }
      // Mark as final.
      this.isFinal = true;
    }

    private string EntityMapKey(Entity e) {
      return $"{e.startPosition}:${e.endPosition}";
    }
 
  }
}