# Zone Shooter – Reinforcement Learning Agent (Unity ML-Agents)

A 3D reinforcement learning project built with **Unity** and **ML-Agents**.
The agent learns to navigate to a randomly selected **active zone** in a square arena, and to **shoot the enemies** that spawn inside that zone, but only while it stands inside the zone itself.

![TensorBoard comparison](docs/tensorboard_compare.png)

---

## The task

- The arena is a square floor divided into **4 zones**.
- At any time, exactly **one zone is active**. It is highlighted in green during playback.
- **Phase 1 (zones only):** the agent has to reach the active zone. When it gets there, a new random zone becomes active.
- **Phase 2 (with enemies):** enemies spawn at random positions inside the active zone. The agent must enter the zone and shoot all of them. When every enemy is down, a new zone is activated and new enemies spawn there.
- Shots fired from outside the active zone are penalised, so the agent learns to **go in first, then shoot**.

The switch from phase 1 to phase 2 is handled automatically by **curriculum learning** (see `ShooterTask.yaml`).

---

## Agent

### Observations

**Vector observations (14 values):**

| Observation | Size |
|---|---|
| Active zone index (one-hot) | 4 |
| Agent position in the arena, normalised to [-1, 1] | 2 |
| Agent facing direction (forward x, z) | 2 |
| Direction to the active zone centre, in the agent's local frame | 2 |
| Distance to the active zone centre (normalised) | 1 |
| Is the agent inside the active zone (0/1) | 1 |
| Can the agent shoot right now (cooldown ready, 0/1) | 1 |
| Remaining enemies (normalised) | 1 |

**Sensor:**
- A **Ray Perception Sensor 3D** detects objects tagged `Enemy` and `Wall`.
- The zone trigger colliders are on a separate layer that the rays ignore, so they don't block the agent's vision.

### Actions

- **2 continuous actions** in [-1, 1]:
  - move forward/backward
  - rotate left/right
- **1 discrete action branch** (size 2): shoot / don't shoot.
- Shooting is **hitscan**, i.e. an instant raycast with a cooldown. A physical bullet could fly straight through enemies at high time scales, so it was replaced.

### Rewards

| Event | Reward |
|---|---|
| Entering the active zone (once per zone activation) | +1.0 |
| Hitting an enemy from inside the active zone | +1.0 |
| Hitting an enemy from outside the active zone | −0.2 |
| Clearing all enemies in the zone | +1.0 |
| Every shot fired | −0.01 |
| Every step (time pressure) | −1 / MaxStep |
| Moving closer to / away from the active zone (shaping) | ± proportional to distance change |

The episode length is `MaxStep = 3000` physics steps. With a Decision Period of 5, that is 600 decisions per episode.

---

## Training

- **Algorithm:** PPO (ML-Agents)
- **Curriculum:**
  - Lesson `ZonesOnly`: no enemies.
  - Lesson `WithEnemies`: starts once the smoothed mean reward stays above 5.0 for at least 100 episodes.
- **Parallel environments:** the whole arena is a `TrainingArea` prefab, duplicated many times in the scene (100 agents in the final runs). All agents train **one shared policy**, which speeds up training a lot and makes it more stable.
- **Randomisation:** each episode randomises the agent's start position and rotation, the order of the active zones, and the enemy spawn positions.

### Hyperparameter comparison

Three configurations were trained for 1M steps each, with the same environment and the same settings otherwise:

| Run | Network | Learning rate | Mean reward @ 1M |
|---|---|---|---|
| `Compare_Small` | 128 units × 2 layers | 3e-4 | ~71 |
| `Compare_Big` | 512 units × 3 layers | 3e-4 | ~82 |
| `Compare_FastLR` | 256 units × 2 layers | 1e-3 | ~83 |

**Observations from the comparison:**
- **FastLR learned the fastest and reached the highest reward.** For this task, the larger learning rate sped up learning without making it unstable.
- **Big performed almost the same as FastLR, but it is more expensive to compute.** A 256-unit network is already enough capacity for this task.
- **Small learned slower and plateaued lower.** It also showed a temporary performance collapse at around 670k steps, from which it recovered. This kind of policy collapse is typical for PPO with a lower-capacity network.
- **All runs show a flat region at the start (≈100k–250k steps).** This is the `ZonesOnly` lesson. The big rise in reward comes after the curriculum switches enemies on.

---

## How to run

### Requirements

- **Unity:** 6000.6.5f1
- **ML-Agents Unity package:** 4.1.0
- **Python (Anaconda env):** `mlagents 0.28.0`, PyTorch 1.10.1

### Training

```bash
conda activate ml-agents-env
cd <project root>
mlagents-learn ShooterTask.yaml --run-id=MyRun
```

When the prompt says *"Start training by pressing the Play button"*, press **Play** in the Unity Editor.

Before you start, check the agent's **Behavior Parameters**:
- Behavior Type: `Default`
- Model field: empty

To continue an existing run, add `--resume` to the command.

To run the hyperparameter comparison:

```bash
mlagents-learn ShooterTask_small.yaml  --run-id=Compare_Small
mlagents-learn ShooterTask_big.yaml    --run-id=Compare_Big
mlagents-learn ShooterTask_fastlr.yaml --run-id=Compare_FastLR
```

### TensorBoard

```bash
tensorboard --logdir results
```

Then open http://localhost:6006.

### Watching the trained agent

1. Drag the `.onnx` model from `results/<run-id>/ShooterTask.onnx` into the Unity project.
2. On the agent's **Behavior Parameters**, set:
   - **Model:** the `.onnx` file
   - **Behavior Type:** `Inference Only`
3. On the agent, enable **`Spawn Enemies Default`**. This is needed because there is no curriculum without Python.
4. Press **Play**.

---

## Project structure

```
Assets/
  Scripts/
    ShooterAgent.cs     # the ML-Agents agent: observations, actions, rewards, hitscan shooting
    GameManager.cs      # active zone logic, enemy spawning, zone highlighting, on-screen info
    Enemy.cs            # marker component for enemies
ShooterTask.yaml        # main training config (PPO + curriculum)
ShooterTask_small.yaml  # comparison: smaller network
ShooterTask_big.yaml    # comparison: bigger network
ShooterTask_fastlr.yaml # comparison: higher learning rate
results/                # training results (TensorBoard logs, .onnx models)
docs/                   # images for this README
```

---

## Demo video

*(Link to the video here.)*
