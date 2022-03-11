using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;

namespace Speechly.SLUClient {
  public delegate string IntentFormatter (string intent);
  public delegate string EntityFormatter (string words, string entityType);

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
      return this.ToString(
        (intent) => $"*{intent}",
        (words, entityType) => $"[{words}]({entityType})",
        " ✔"
      );
    }

    public string ToString(
        IntentFormatter intentTag,
        EntityFormatter entityTag,
        string confirmationMark
      ) {
      var sb = new StringBuilder();
      var entityIds = new string[words.Length];
      foreach (KeyValuePair<string, Entity> entity in entities) {
        for (var i = entity.Value.startPosition; i < entity.Value.endPosition; i++) {
          entityIds[i] = entity.Key;
        }
      }

      sb.Append(intentTag(this.intent.intent));
      
      bool firstWord = this.intent.intent == "";
      string lastEntityId = null;

      for (int i = 0; i < words.Length; i++) {
        var word = words[i];
        if (word == null) continue;


        if (entityIds[i] != lastEntityId) {
          if (lastEntityId != null) {
            var entity = entities[lastEntityId];
            if (!firstWord) sb.Append(" ");
            sb.Append(entityTag(entity.value, entity.type));
            firstWord = false;
          }
        }

        if (entityIds[i] == null) {
          if (!firstWord) sb.Append(" ");
          sb.Append(word.word);
          firstWord = false;
        }

        lastEntityId = entityIds[i];
      }

      if (lastEntityId != null) {
        if (!firstWord) sb.Append(" ");
        var entity = entities[lastEntityId];
        sb.Append(entityTag(entity.value, entity.type));
      }

      if (this.isFinal) sb.Append(confirmationMark);

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