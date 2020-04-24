// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TimingConfig;

public class GuitarNoteHitAndMissDetect {

    public enum MissSubType
    {
        NoteMiss,
        Overstrum,
    }

    public delegate void HitNoteFactory(float time, GuitarNoteHitKnowledge noteHitKnowledge);
    public delegate void MissNoteFactory(float time, MissSubType missSubType, GuitarNoteHitKnowledge noteHitKnowledge);

    HitNoteFactory m_hitNoteFactory;
    MissNoteFactory m_missNoteFactory;

    int previousInputMask;
    bool canTap;
    GuitarNoteHitKnowledge lastNoteHit = null;

    public void Reset()
    {
        previousInputMask = 0;
        canTap = true;
        lastNoteHit = null;
    }

    public GuitarNoteHitAndMissDetect(HitNoteFactory hitNoteFactory, MissNoteFactory missNoteFactory)
    {
        m_hitNoteFactory = hitNoteFactory;
        m_missNoteFactory = missNoteFactory;
    }

    private void WriteToLog(string stringToLog)
    {
        var logging = false;
        if (logging)
        {
            System.IO.File.AppendAllText(@"C:\Users\Mark\Documents\build\Log.txt", stringToLog + "\n");
        }
    }

    private void WriteInputToChart(float time, int inputMask)
    {
        var log = time + " Started\n";
        WriteToLog(log);
        
        ChartEditor editor = ChartEditor.Instance;
        Song currentSong = editor.currentSong;

        var tick = currentSong.TimeToTick(time, currentSong.resolution);
        int strummedTick = (int)tick;
        
        WriteToLog("Time: " + time +
                   "\nResolution: " + currentSong.resolution +
                   "\nTick: " + strummedTick);

        int multiplyFactor = 4;
        int step = GameSettings.step; // snap notes to the step selected

        int resolution = (int)currentSong.resolution;

        float denominator = multiplyFactor / (float)step;
        int factor = (int)(resolution * denominator);

        log = "Denominator: " + denominator + "\nFactor: " + factor + "\n";
        WriteToLog(log);

        int roundedPosition = strummedTick / factor;
        int roundedTick = roundedPosition * factor;
        uint u_roundedTick = (uint)roundedTick;
        
        WriteToLog("roundedPosition: " + roundedPosition +
                   "\nroundedTick: " + roundedTick +
                   "\nu_roundedTick: " + u_roundedTick);

        // map input to frets
        var fret = Note.GuitarFret.Green;
        if (inputMask == 1)
        {
            fret = Note.GuitarFret.Green;
        }
        else if (inputMask == 2)
        {
            fret = Note.GuitarFret.Red;
        }
        else if (inputMask == 4)
        {
            fret = Note.GuitarFret.Yellow;
        }
        else if (inputMask == 8)
        {
            fret = Note.GuitarFret.Blue;
        }
        else if (inputMask == 16)
        {
            fret = Note.GuitarFret.Orange;
        }

        // write note to chart
        var note = new Note(u_roundedTick, fret);

        List<SongObject> currentlyAddingNotes = new List<SongObject>();
        currentlyAddingNotes.Add(note);

        editor.commandStack.Push(new SongEditAdd(currentlyAddingNotes));

        log = time + " Finished\n";
        WriteToLog(log);
    }

    public void Update (float time, HitWindow<GuitarNoteHitKnowledge> hitWindow, uint noteStreak, GuitarSustainHitKnowledge sustainKnowledge)
    {
        // Capture input
        bool strum = GuitarInput.GetStrumInput();
        int inputMask = GuitarInput.GetFretInputMask();
        if (inputMask != previousInputMask)
            canTap = true;

        if (strum)
        {
            WriteInputToChart(time, inputMask);
        }

        // What note is the player trying to hit next?
        GuitarNoteHitKnowledge nextNoteToHit = hitWindow.oldestUnhitNote;

        UpdateNoteKnowledge(time, hitWindow, inputMask, strum, noteStreak, nextNoteToHit, sustainKnowledge);

        if (nextNoteToHit != null)
        {
            Note nextSeperate = nextNoteToHit.note.nextSeperateNote;

            if (noteStreak > 0)
            {
                PreserveStreakDetect(time, hitWindow, strum, noteStreak, nextNoteToHit, inputMask);
            }
            else
            {
                RecoveryDetect(time, hitWindow, inputMask, strum, noteStreak);
            }
        }
        // No note in window
        else
        {
            BlankWindowDetect(time, strum);
        }

        previousInputMask = inputMask;
    }

    void UpdateNoteKnowledge(float time, HitWindow<GuitarNoteHitKnowledge> hitWindow, int inputMask, bool strummed, uint noteStreak, GuitarNoteHitKnowledge nextNoteToHit, GuitarSustainHitKnowledge sustainKnowledge)
    {
        // Check if it's valid to query the last hit note
        if (noteStreak <= 0 || lastNoteHit == null || !hitWindow.IsWithinTimeWindow(lastNoteHit.note, nextNoteToHit != null ? nextNoteToHit.note : null, time))
        {
            lastNoteHit = null;
        }

        if (nextNoteToHit != null && noteStreak > 0)    // None of this knowledge should be used for recovery
        {
            if (nextNoteToHit.strumCounter > 1)
                nextNoteToHit.strumCounter = 1;     // Make this still valid to hit because it's still in the hit window for a reason

            // Fill out note knowledge
            if (GameplayInputFunctions.ValidateFrets(nextNoteToHit.note, inputMask, noteStreak, sustainKnowledge.extendedSustainsMask))
                nextNoteToHit.fretValidationTime = time;
            else
                nextNoteToHit.lastestFretInvalidationTime = time;

            if (GameplayInputFunctions.ValidateStrum(nextNoteToHit.note, canTap, strummed, noteStreak))
                nextNoteToHit.strumValidationTime = time;
            else
                nextNoteToHit.lastestStrumInvalidationTime = time;

            if (strummed)
            {
                if (lastNoteHit != null && lastNoteHit.strumCounter <= 0)// lastNoteHit.note.type != Note.Note_Type.Strum)
                    ++lastNoteHit.strumCounter;
                else
                    ++nextNoteToHit.strumCounter;
            }
        }
    }

    void PreserveStreakDetect(float time, HitWindow<GuitarNoteHitKnowledge> hitWindow, bool strummed, uint noteStreak, GuitarNoteHitKnowledge nextNoteToHit, int inputMask)
    {
        if (nextNoteToHit.strumCounter > 1)
        {
            MissNote(time, MissSubType.Overstrum);
            Debug.Log("Missed note due to double strumming on a single note");
        }
        else if (nextNoteToHit.fretsValidated && nextNoteToHit.strumValidated && Mathf.Abs(nextNoteToHit.fretValidationTime - nextNoteToHit.strumValidationTime) <= GuitarTiming.slopBufferTime)
        {
            HitNote(time, nextNoteToHit);
        }
        else if (nextNoteToHit.strumValidated && Mathf.Abs(time - nextNoteToHit.strumValidationTime) > GuitarTiming.slopBufferTime && nextNoteToHit.strumCounter > 0)
        {
            MissNote(time, MissSubType.Overstrum);
            Debug.Log("Missed note due to strum expiration");

            nextNoteToHit.strumValidationTime = GuitarNoteHitKnowledge.NULL_TIME;
        }
    }

    void RecoveryDetect(float time, HitWindow<GuitarNoteHitKnowledge> hitWindow, int fretInputMask, bool strummed, uint noteStreak)
    {
        var noteKnowledgeList = hitWindow.noteKnowledgeQueue;

        // Search to see if user is hitting a note ahead
        List<GuitarNoteHitKnowledge> validatedNotes = new List<GuitarNoteHitKnowledge>();
        foreach (GuitarNoteHitKnowledge noteKnowledge in noteKnowledgeList)
        {
            // Collect all notes the user is possibly hitting
            if (
                    GameplayInputFunctions.ValidateFrets(noteKnowledge.note, fretInputMask, noteStreak)
                    && GameplayInputFunctions.ValidateStrum(noteKnowledge.note, canTap, strummed, noteStreak)
                )
                validatedNotes.Add(noteKnowledge);
        }

        if (validatedNotes.Count > 0)
        {
            // Recovery algorithm
            // Select the note closest to the strikeline
            float aimYPos = ChartEditor.Instance.visibleStrikeline.transform.position.y + 0.25f;  // Added offset from the note controller

            GuitarNoteHitKnowledge selectedNote = validatedNotes[0];

            float dis = -1;

            foreach (GuitarNoteHitKnowledge validatedNote in validatedNotes)
            {
                if (!selectedNote.note.controller)
                    return;

                NoteController noteController = selectedNote.note.controller;

                float distance = Mathf.Abs(aimYPos - noteController.transform.position.y);
                if (distance < dis || dis < 0)
                {
                    selectedNote = validatedNote;
                    dis = distance;
                }
            }

            int index = noteKnowledgeList.IndexOf(selectedNote);
            GuitarNoteHitKnowledge note = noteKnowledgeList[index];

            // Recovery missed notes
            if (index > 0)
                Debug.Log("Missed notes when performing recovery. Notes skipped = " + index);

            for (int missedCounter = 0; missedCounter < index; ++missedCounter)
            {
                MissNote(time, MissSubType.NoteMiss, noteKnowledgeList[missedCounter]);
            }

            HitNote(time, note);

            // We fill out our own knowledge
            note.fretValidationTime = time;
            note.strumValidationTime = time;
            if (strummed)
                ++note.strumCounter;
        }
        else if (strummed)
        {
            MissNote(time, MissSubType.Overstrum);
            Debug.Log("Missed due to strumming when there were no notes to strum during recovery");
        }
    }

    void BlankWindowDetect(float time, bool strummed)
    {
        if (strummed)
        {
            // Are we strumming late for a hopo/tap?
            if (lastNoteHit != null && lastNoteHit.note.type != Note.NoteType.Strum && lastNoteHit.strumCounter <= 1)
            {
                lastNoteHit = null;
            }
            else
            {
                MissNote(time, MissSubType.Overstrum);
                Debug.Log("Missed due to strum input when no note was present in the window");
            }
        }
    }

    void HitNote(float time, GuitarNoteHitKnowledge noteHitKnowledge)
    {
        m_hitNoteFactory(time, noteHitKnowledge);
        canTap = false;
        lastNoteHit = noteHitKnowledge;
    }

    void MissNote(float time, MissSubType missSubType, GuitarNoteHitKnowledge noteHitKnowledge = null)
    {
        m_missNoteFactory(time, missSubType, noteHitKnowledge);
    }
}
