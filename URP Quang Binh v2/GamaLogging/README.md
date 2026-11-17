# Main-scene study logging

The logger attaches only when `SimulationManager` is in
`Main Scene - ArtUpdate_Flood`. It does not call or change `FloodStudyLogger`
and does not modify `Tutorial_Scene`.

## Output

```
Application.persistentDataPath/FloodStudyLogs/
  Participant_XX/                         # or Participant_Unknown
    MainScene_<UTC run id>_Game_01/
      Main_Round_001.json
      Main_Round_001_build_end_<UTC>.png
      Main_Round_001_build_end_<UTC>.json
      Main_Round_001_simulation_end_<UTC>.png
      Main_Round_001_simulation_end_<UTC>.json
      events.jsonl
      rounds.csv
```

Round JSON uses tutorial-compatible identity, UTC time, duration, dyke length,
count and screenshot fields. It adds separate `dykes` and `dams` lists with
GAMA names, ownership, original endpoints, original/remaining length, build
confirmation, manual destruction and flooding/presence flags. Positions
labelled `local` use the unscaled GAMA map origin, explicitly described in
`coordinate_reference`; the scene's decorative MAP offset is not applied.

`dyke_length`, `dam_length`, `evacuated`, `casualties`, and `score` are the
authoritative GAMA globals for that round. `build_end` and `simulation_end`
retain separate metric snapshots. Built/destroyed lengths count confirmed
actions, rather than being inferred from how long a renderer appears or
whether it disappears under water. Joining mid-round records existing
structures as observations, without inventing past build timestamps/counts.
The CSV uses the latest values for incomplete rounds and the final snapshot
for completed rounds. `status` distinguishes completed/interrupted runs.

Participant metadata is taken from the existing `ParticipantSession` when
available. Starting the main scene directly does not invent a check-in:
it uses `Participant_Unknown` and records `connection_id` for identification.

## Required GAMA changes

The two prepared model files add logging only:

* `Flooding Model.gaml` retains each dyke's original line endpoints before
  its geometry is buffered.
* `Flooding VR.gaml` sends confirmed build/destruction events and per-frame
  round metrics. At each phase end it forces a final geometry packet marked
  `build_end`/`simulation_end`. The latter is sent **before**
  `exit_flooding_base` increments the round and clears/resets data.

The normal `send_agents` reflex and the forced snapshots use the same
geometry-queue action. This includes the final water geometry, not flooded
building outcome records. Unity preserves terminal packets in order and
renders their screenshots only after applying geometry, attributes and
removals, before the next round can replace the scene.

Images are 1024x1024 PNG, like the tutorial, with a fixed overhead view framed
from active MAP renderers. UI canvases and ray/preview lines are temporarily
hidden and restored even on render failure. Failed captures have an explicit
`capture_succeeded: false` manifest and an empty image path.

`Prepare-MainSceneLogging.ps1` prepares full files from the current originals
in both `C:/Users/GIGABYTE/Gama_Workspace/Gama` and the WebPlatform learning package.
It verifies every expected edit. `-Install` checks original and prepared
SHA-256 hashes for all four named files before writing any destination.
The install requires permission because these folders are outside the Unity
workspace's writable root. Reload/restart the GAMA experiment after installing,
and rebuild the APK if testing on Quest.

An unmodified GAMA model continues to work but does not provide these logs;
Unity warns after 30 seconds rather than fabricating unavailable statistics.

## Validation

* `Tests/Run-Checks.ps1`: compiles the actual record accumulator and packet
  queue with a minimal Vector3 shim. Checks confirmations, duplicates, rejected
  actions, flooding vs manual destruction, round isolation, coordinate axes,
  full-precision metrics, and ordering/preservation of terminal frames.
* `Tests/Validate-Gama.ps1`: sends the read-only `describe` command to the
  installed local GAMA server. It compiles prepared model copies against copied
  GIS inputs in Unity's Temp folder; it does not start/change an experiment.
* Unity scripts were compiled using the project's actual compiler response
  file/references, writing validation assemblies only under Temp.

An integrated round on the Quest/Unity renderer is still needed to verify
image appearance and the complete live GAMA-to-Unity sequence.
