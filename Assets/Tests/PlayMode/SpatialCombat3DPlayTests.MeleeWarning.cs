using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Tests.PlayMode
{
    public sealed partial class SpatialCombat3DPlayTests
    {
        [UnityTest]
        public IEnumerator DangerousMelee3D_WhenHostileKnightSharesTheWorld_PlaysAuthoredCueOnceDuringAnticipation()
        {
            CharacterActor3D knight = CreateActor(_room, "Knight", new Vector3(0f, 0.1f, 2f),
                CharacterBelong.Ally, CharacterFeature.Player);
            CharacterActor3D enemy = CreateActor(_room, "Hostile attacker", new Vector3(0f, 0.1f, 0f));
            CharacterActor3D instant = CreateActor(_room, "Instant attacker", new Vector3(4f, 0.1f, 0f));
            CharacterActor3D friend = CreateActor(_room, "Friendly attacker", new Vector3(2f, 0.1f, 0f), CharacterBelong.Ally);
            CharacterActor3D remote = CreateActor(_otherRoom, "Other-world attacker", new Vector3(0f, 0.1f, 0f));
            CreateActor(_otherRoom, "Other-world nonplayer", new Vector3(0f, 0.1f, 2f), CharacterBelong.Ally);
            yield return null;
            SynchronizeWorld(_room);
            SynchronizeWorld(_otherRoom);

            AudioClip clip = AudioClip.Create("Authored 3D warning", 22050, 1, 22050, false);
            _assets.Add(clip);
            AttackInfo ordinary = CreateAttack(AttackShape3D.Melee);
            AttackInfo dangerous = CreateAttack(AttackShape3D.Melee);
            dangerous.feature = AttackFeature.JustGuardImmune;
            dangerous.effectSoundInfo.guardWarningSound = clip;
            AttackInfo noWindup = CreateAttack(AttackShape3D.Melee);
            noWindup.feature = AttackFeature.JustGuardImmune;
            noWindup.motion.info.preMotionDuration = 0f;
            noWindup.effectSoundInfo.guardWarningSound = clip;

            AssertSilentMeleeCue(enemy, ordinary);
            AssertSilentMeleeCue(friend, dangerous);
            AssertSilentMeleeCue(remote, dangerous);
            AssertSilentMeleeCue(instant, noWindup);

            Assert.That(GameManager.Actions.Execute3D(enemy, dangerous, out long instanceId), Is.True);
            Assert.That(GameManager.Actions.Execution.TryGetInstance(instanceId, out ActionExecution windup), Is.True);
            Assert.That(windup.phase, Is.EqualTo(AnimationPhase.Anticipation));
            AudioSource source = FindMeleeWarningAudio();
            Assert.That(source, Is.Not.Null);
            Assert.That(source.clip, Is.SameAs(clip));
            Assert.That(source.isPlaying, Is.True);
            Assert.That(_hits, Is.Empty);

            List<ProjectileLockWarning> warnings = new List<ProjectileLockWarning>();
            GameManager.Projectiles3D.CollectLockWarnings(knight.Actor, warnings);
            Assert.That(warnings, Is.Empty);
            Assert.That(VisibleWarning(), Is.Null);

            // Clearing the clip makes another presentation call observable without waiting on the audio device.
            source.Stop();
            source.clip = null;
            Step(0.1f);
            Step(0.12f);
            AssertSingleHit(enemy, knight, instanceId);
            Step(0.45f);
            Assert.That(FindMeleeWarningAudio(), Is.SameAs(source));
            Assert.That(source.clip, Is.Null);
            Assert.That(source.isPlaying, Is.False);
        }

        [UnityTest]
        public IEnumerator DangerousMelee3D_WithoutAnAuthoredClip_PlaysTheAudibleFallback()
        {
            CreateActor(_room, "Knight", new Vector3(0f, 0.1f, 2f), CharacterBelong.Ally, CharacterFeature.Player);
            CharacterActor3D enemy = CreateActor(_room, "Hostile attacker", new Vector3(0f, 0.1f, 0f));
            yield return null;

            AttackInfo dangerous = CreateAttack(AttackShape3D.Melee);
            dangerous.feature = AttackFeature.JustGuardImmune;
            Assert.That(GameManager.Actions.Execute3D(enemy, dangerous, out _), Is.True);

            AudioSource source = FindMeleeWarningAudio();
            Assert.That(source, Is.Not.Null);
            Assert.That(source.clip, Is.Not.Null);
            Assert.That(source.isPlaying, Is.True);
            Assert.That(source.clip.samples, Is.GreaterThan(0));
            float[] samples = new float[source.clip.samples];
            Assert.That(source.clip.GetData(samples, 0), Is.True);
            Assert.That(System.Array.Exists(samples, sample => Mathf.Abs(sample) > 0.1f), Is.True);
        }

        private static void AssertSilentMeleeCue(CharacterActor3D attacker, AttackInfo attack)
        {
            Assert.That(GameManager.Actions.Execute3D(attacker, attack, out long instanceId), Is.True);
            Assert.That(FindMeleeWarningAudio(), Is.Null);
            Assert.That(GameManager.Actions.Execution.Cancel(instanceId, force: true), Is.True);
        }

        private static AudioSource FindMeleeWarningAudio()
        {
            Transform warning = GameManager.Instance.transform.Find("[Dangerous Melee Warning]");
            return warning != null ? warning.GetComponent<AudioSource>() : null;
        }
    }
}
