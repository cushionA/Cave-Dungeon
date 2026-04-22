using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Game.Core;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// AnimationBridge の性能改修テスト。
    /// - パラメータキーが Animator.StringToHash 相当の int hash で管理されている
    /// - 同じ値を 2 回書いても Flush では 1 回しか送信されない
    /// - ダーティ判定はパラメータ単位で、変更があったパラメータだけが再送対象になる
    /// を検証する。
    /// </summary>
    [TestFixture]
    public class AnimationBridgeOptimizationTests
    {
        private AnimationBridge _bridge;

        [SetUp]
        public void SetUp()
        {
            _bridge = new AnimationBridge();
        }

        // ─── H1: string → int hash 化 ───

        [Test]
        public void SetFloat_StringとIntHash_同じキーとして扱われる()
        {
            int hash = Animator.StringToHash("Speed");
            _bridge.SetFloat("Speed", 5.0f);

            Assert.AreEqual(5.0f, _bridge.GetFloat(hash), 0.001f);
        }

        [Test]
        public void SetBool_StringとIntHash_同じキーとして扱われる()
        {
            int hash = Animator.StringToHash("IsGrounded");
            _bridge.SetBool("IsGrounded", true);

            Assert.IsTrue(_bridge.GetBool(hash));
        }

        [Test]
        public void SetInt_StringとIntHash_同じキーとして扱われる()
        {
            int hash = Animator.StringToHash("WeaponType");
            _bridge.SetInt("WeaponType", 7);

            Assert.AreEqual(7, _bridge.GetInt(hash));
        }

        [Test]
        public void SetTrigger_StringとIntHash_同じキーとして扱われる()
        {
            int hash = Animator.StringToHash("Attack");
            _bridge.SetTrigger("Attack");

            Assert.IsTrue(_bridge.ConsumeTrigger(hash));
        }

        [Test]
        public void Floats_内部キーがIntHash型で取得できる()
        {
            _bridge.SetFloat("Speed", 1.0f);

            int expectedHash = Animator.StringToHash("Speed");
            IReadOnlyDictionary<int, float> floats = _bridge.Floats;
            Assert.IsTrue(floats.ContainsKey(expectedHash));
            Assert.AreEqual(1.0f, floats[expectedHash], 0.001f);
        }

        [Test]
        public void SetFloat_IntHashで直接設定_値が取得できる()
        {
            int hash = Animator.StringToHash("MoveX");
            _bridge.SetFloat(hash, 3.5f);

            Assert.AreEqual(3.5f, _bridge.GetFloat(hash), 0.001f);
            Assert.AreEqual(3.5f, _bridge.GetFloat("MoveX"), 0.001f);
        }

        // ─── H2: パラメータ単位ダーティフラグ ───

        [Test]
        public void SetFloat_変更されたパラメータのみDirtyFloatsに含まれる()
        {
            _bridge.SetFloat("Speed", 1.0f);
            _bridge.SetFloat("MoveX", 2.0f);

            int speedHash = Animator.StringToHash("Speed");
            int moveXHash = Animator.StringToHash("MoveX");

            Assert.IsTrue(_bridge.DirtyFloats.Contains(speedHash), "Speed が dirty 集合に含まれる");
            Assert.IsTrue(_bridge.DirtyFloats.Contains(moveXHash), "MoveX が dirty 集合に含まれる");
            Assert.AreEqual(2, _bridge.DirtyFloats.Count);
        }

        [Test]
        public void SetFloat_同じ値再設定_Dirtyにならない()
        {
            _bridge.SetFloat("Speed", 1.0f);
            _bridge.ClearDirty();

            _bridge.SetFloat("Speed", 1.0f);

            Assert.AreEqual(0, _bridge.DirtyFloats.Count);
            Assert.IsFalse(_bridge.IsDirty);
        }

        [Test]
        public void SetFloat_同じ値を2回書く_Flushで1回だけ送信される()
        {
            // Flush を模擬: DirtyFloats を読み取って現在値を記録し、ClearDirty でリセット。
            // 2 回同じ値を書いた場合、Flush 時に送信されるのは 1 回のみであることを検証する。
            _bridge.SetFloat("Speed", 1.0f);
            _bridge.SetFloat("Speed", 1.0f);

            List<KeyValuePair<int, float>> sent = CaptureFloatFlush(_bridge);

            Assert.AreEqual(1, sent.Count, "同じ値を2回書いても送信は1回のみ");
            Assert.AreEqual(Animator.StringToHash("Speed"), sent[0].Key);
            Assert.AreEqual(1.0f, sent[0].Value, 0.001f);
        }

        [Test]
        public void SetFloat_異なる値を2回書く_Flushで最終値が1回送信される()
        {
            _bridge.SetFloat("Speed", 1.0f);
            _bridge.SetFloat("Speed", 2.0f);

            List<KeyValuePair<int, float>> sent = CaptureFloatFlush(_bridge);

            Assert.AreEqual(1, sent.Count, "同じキーは集約されて1回のみ送信される");
            Assert.AreEqual(Animator.StringToHash("Speed"), sent[0].Key);
            Assert.AreEqual(2.0f, sent[0].Value, 0.001f);
        }

        [Test]
        public void ClearDirty_全パラメータDirty集合がクリアされる()
        {
            _bridge.SetFloat("Speed", 1.0f);
            _bridge.SetBool("IsGrounded", true);
            _bridge.SetInt("WeaponType", 3);

            _bridge.ClearDirty();

            Assert.AreEqual(0, _bridge.DirtyFloats.Count);
            Assert.AreEqual(0, _bridge.DirtyBools.Count);
            Assert.AreEqual(0, _bridge.DirtyInts.Count);
            Assert.IsFalse(_bridge.IsDirty);
        }

        [Test]
        public void ClearDirty後_変更があったパラメータだけが再度Dirtyになる()
        {
            _bridge.SetFloat("Speed", 1.0f);
            _bridge.SetFloat("MoveX", 2.0f);
            _bridge.ClearDirty();

            // Speed のみ値を変更する
            _bridge.SetFloat("Speed", 3.0f);
            // MoveX は同じ値で書き直す
            _bridge.SetFloat("MoveX", 2.0f);

            int speedHash = Animator.StringToHash("Speed");
            int moveXHash = Animator.StringToHash("MoveX");

            Assert.AreEqual(1, _bridge.DirtyFloats.Count, "変更があったパラメータだけがDirty対象になる");
            Assert.IsTrue(_bridge.DirtyFloats.Contains(speedHash), "Speed は dirty に含まれる");
            Assert.IsFalse(_bridge.DirtyFloats.Contains(moveXHash), "MoveX は同じ値再書き込みなので dirty に含まれない");
        }

        [Test]
        public void SetBool_パラメータ単位Dirty_他の型に影響しない()
        {
            _bridge.SetFloat("Speed", 1.0f);
            _bridge.ClearDirty();

            _bridge.SetBool("IsGrounded", true);

            Assert.AreEqual(0, _bridge.DirtyFloats.Count);
            Assert.AreEqual(1, _bridge.DirtyBools.Count);
            Assert.AreEqual(0, _bridge.DirtyInts.Count);
        }

        [Test]
        public void SetInt_パラメータ単位Dirty_他の型に影響しない()
        {
            _bridge.SetBool("IsGrounded", true);
            _bridge.ClearDirty();

            _bridge.SetInt("WeaponType", 3);

            Assert.AreEqual(0, _bridge.DirtyFloats.Count);
            Assert.AreEqual(0, _bridge.DirtyBools.Count);
            Assert.AreEqual(1, _bridge.DirtyInts.Count);
        }

        [Test]
        public void SetBool_同じ値再設定_Dirtyにならない()
        {
            _bridge.SetBool("IsGrounded", true);
            _bridge.ClearDirty();
            _bridge.SetBool("IsGrounded", true);

            Assert.AreEqual(0, _bridge.DirtyBools.Count);
            Assert.IsFalse(_bridge.IsDirty);
        }

        [Test]
        public void SetInt_同じ値再設定_Dirtyにならない()
        {
            _bridge.SetInt("WeaponType", 3);
            _bridge.ClearDirty();
            _bridge.SetInt("WeaponType", 3);

            Assert.AreEqual(0, _bridge.DirtyInts.Count);
            Assert.IsFalse(_bridge.IsDirty);
        }

        [Test]
        public void Flush模擬_2回目のFlushで値が変化していなければ何も送信されない()
        {
            // 1回目: 値を設定してFlush
            _bridge.SetFloat("Speed", 1.0f);
            _bridge.SetBool("IsGrounded", true);
            _bridge.SetInt("WeaponType", 3);

            List<KeyValuePair<int, float>> firstFloatFlush = CaptureFloatFlush(_bridge);
            Assert.AreEqual(1, firstFloatFlush.Count);

            // 2回目: 同じ値で書き直す → 何も送信されない
            _bridge.SetFloat("Speed", 1.0f);
            _bridge.SetBool("IsGrounded", true);
            _bridge.SetInt("WeaponType", 3);

            List<KeyValuePair<int, float>> secondFloatFlush = CaptureFloatFlush(_bridge);
            Assert.AreEqual(0, secondFloatFlush.Count, "値が変わっていなければFlushで再送信されない");
            Assert.IsFalse(_bridge.IsDirty);
        }

        [Test]
        public void IsDirty_phaseDirtyとparameterDirtyを統合して返す()
        {
            // 初期状態は false
            Assert.IsFalse(_bridge.IsDirty);

            // パラメータ設定で true
            _bridge.SetFloat("Speed", 1.0f);
            Assert.IsTrue(_bridge.IsDirty);

            _bridge.ClearDirty();
            Assert.IsFalse(_bridge.IsDirty);

            // フェーズ変更で true
            MotionInfo motion = new MotionInfo
            {
                preMotionDuration = 0.1f,
                activeMotionDuration = 0.1f,
                recoveryDuration = 0.1f
            };
            _bridge.StartActionPhase(motion, 1);
            Assert.IsTrue(_bridge.IsDirty);

            _bridge.ClearDirty();
            Assert.IsFalse(_bridge.IsDirty);

            // トリガーで true
            _bridge.SetTrigger("Jump");
            Assert.IsTrue(_bridge.IsDirty);
        }

        // ─── Helper ───

        /// <summary>
        /// Flush 処理を模擬する。DirtyFloats 経由で変更があったキーだけを取得し、
        /// ClearDirty() でダーティ集合をクリアして呼び出し側の Animator 反映を再現する。
        /// </summary>
        private static List<KeyValuePair<int, float>> CaptureFloatFlush(AnimationBridge bridge)
        {
            List<KeyValuePair<int, float>> captured = new List<KeyValuePair<int, float>>();
            foreach (int hash in bridge.DirtyFloats)
            {
                captured.Add(new KeyValuePair<int, float>(hash, bridge.GetFloat(hash)));
            }
            bridge.ClearDirty();
            return captured;
        }
    }
}
