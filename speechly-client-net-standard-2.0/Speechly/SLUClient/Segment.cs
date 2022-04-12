using System;
using System.Text;
using System.Collections.Generic;
using Speechly.Types;

namespace Speechly.SLUClient {

/// <summary>
/// A high level API for automatic speech recognition (ASR) and natural language understanding (NLU) results.
/// Results will accumulate in Segment for the duration of the an utterance.
/// </summary>
  public class Segment {
    public int id;
    public string contextId;
    public bool isFinal = false;
    public Word[] words = new Word[0];
    public Dictionary<string, Entity> entities = new Dictionary<string, Entity>();
    public Intent intent = new Intent{ intent = "", isFinal = false };

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
        BeautifyIntent intentTag,
        BeautifyEntity entityTag,
        string confirmationMark
      ) {
      var sb = new StringBuilder();

      lock(this) {
        var entityIds = new string[words.Length];
        foreach (KeyValuePair<string, Entity> entity in entities) {
          for (var i = entity.Value.startPosition; i < entity.Value.endPosition; i++) {
            entityIds[i] = entity.Key;
          }
        }

        sb.Append(intentTag(this.intent.intent));
        
        bool firstWord = sb.Length == 0;
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
      }

      return sb.ToString();
    }

    internal void UpdateTranscript(Word word) {
      lock(this) {
        if (word.index >= words.Length) {
          Array.Resize(ref words, word.index + 1);
        }
        words[word.index] = word;
      }
    }

    internal void UpdateTranscript(Word[] words) {
      lock(this) {
        foreach(Word w in words) UpdateTranscript(w);
      }
    }

    internal void UpdateEntity(Entity entity) {
      lock(this) {
        entities[EntityMapKey(entity)] = entity;
      }
    }

    internal void UpdateEntity(Entity[] entities) {
      lock(this) {
        foreach(Entity entity in entities) UpdateEntity(entity);
      }
    }

    internal void UpdateIntent(string intent, bool isFinal) {
      lock(this) {
        this.intent.intent = intent;
        this.intent.isFinal = isFinal;
      }
    }

    internal void EndSegment() {
      lock(this) {
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
    }

    private string EntityMapKey(Entity e) {
      return $"{e.startPosition}:${e.endPosition}";
    }
 
  }
}