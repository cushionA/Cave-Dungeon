using CharacterController;
using CharacterController.StatusData;
using MoreMountains.CorgiEngine;
using MoreMountains.Feedbacks;
using MoreMountains.Tools;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using static BattleMoveData;
using static CharacterController.StatusData.BrainStatus;

namespace MoreMountains.CorgiEngine
{

    /// <summary>
    /// このクラスはオブジェクトのヘルス（体力）を管理し、ヘルスバーを制御し、ダメージを受けた際の処理、
    /// および死亡時の処理を担当します。
    /// </summary>
    [AddComponentMenu("Corgi Engine/Character/Core/MyHealth")]
    public class MyHealth : Health
    {

        #region 定義

        /// <summary>
        /// ガードチェック時に使う
        /// </summary>
        protected enum GuardState : byte
        {
            ガードしない = 1 << 0,
            ガード成功 = 1 << 1,
            ガードブレイク = (1 << 2),
            パリィ = (1 << 3),
            強化ガード = (1 << 4),
        }

        #endregion 定義


        [MMInspectorGroup("ステータス", true, 1)]

        /// trueの場合、このオブジェクトは現在ダメージを受けることができません（一時的な無敵状態）
        [MMReadOnly]
        [Tooltip("trueの場合、このオブジェクトは現在ダメージを受けることができません（一時的な無敵状態）")]
        public bool TemporarilyInvulnerable = false;

        /// trueの場合、このオブジェクトはダメージ後の無敵状態にあります（連続ダメージ防止）
        [MMReadOnly]
        [Tooltip("trueの場合、このオブジェクトはダメージ後の無敵状態にあります（連続ダメージ防止）")]
        public bool PostDamageInvulnerable = false;

        [MMInformation(
            "このコンポーネントをオブジェクトに追加すると、体力を持ち、ダメージを受けて死亡する可能性があります。",
            MoreMountains.Tools.MMInformationAttribute.InformationType.Info, false)]

        [MMInspectorGroup("体力設定", true, 2)]
        /// trueの場合、このオブジェクトはダメージを受けません（完全無敵）
        [Tooltip("trueの場合、このオブジェクトはダメージを受けません（完全無敵）")]
        public bool Invulnerable = false;

        [MMInspectorGroup("ダメージ設定", true, 3)]

        [MMInformation(
            "ここでは、オブジェクトがダメージを受けた時にインスタンス化するエフェクトとサウンドFX、ヒット時にオブジェクトが点滅する時間を指定できます（スプライトでのみ動作）。",
            MoreMountains.Tools.MMInformationAttribute.InformationType.Info, false)]

        /// このHealthオブジェクトがダメージを受けられるかどうか。Invulnerableの上に追加で使用でき、一時的な無敵のためにオン/オフされます。ImmuneToDamageはより永続的な解決策です。
        [Tooltip("このHealthオブジェクトがダメージを受けられるかどうか。Invulnerableの上に追加で使用でき、一時的な無敵のためにオン/オフされます。ImmuneToDamageはより永続的な解決策です。")]
        public bool ImmuneToDamage = false;

        /// キャラクターがヒットした時に再生するMMFeedbacks
        [Tooltip("キャラクターがヒットした時に再生するMMFeedbacks")]
        public MMFeedbacks DamageFeedbacks;

        /// trueの場合、致命的なヒットかどうかに関係なくDamageFeedbackが再生されます
        [Tooltip("trueの場合、致命的なヒットかどうかに関係なくDamageFeedbackが再生されます")]
        public bool TriggerDamageFeedbackOnDeath = true;

        /// trueの場合、ダメージ値がMMFeedbacksのIntensityパラメータとして渡され、ダメージが増加するにつれてより強烈なフィードバックをトリガーできます
        [Tooltip("trueの場合、ダメージ値がMMFeedbacksのIntensityパラメータとして渡され、ダメージが増加するにつれてより強烈なフィードバックをトリガーできます")]
        public bool FeedbackIsProportionalToDamage = false;

        /// スプライト（存在する場合）がダメージを受けた時に点滅するかどうか
        [Tooltip("スプライト（存在する場合）がダメージを受けた時に点滅するかどうか")]
        public bool FlickerSpriteOnHit = true;

        [MMInspectorGroup("ノックバック設定", true, 6)]

        /// このオブジェクトがノックバックを受けられるかどうか
        [Tooltip("このオブジェクトがノックバックを受けられるかどうか")]
        public bool ImmuneToKnockback = false;

        [MMInspectorGroup("死亡設定", true, 7)]

        [MMInformation(
            "ここでは、オブジェクトが死亡した時にインスタンス化するエフェクト、適用する力（corgiコントローラーが必要）、ゲームスコアに追加するポイント数、キャラクターがリスポーンする場所（非プレイヤーキャラクターのみ）を設定できます。",
            MoreMountains.Tools.MMInformationAttribute.InformationType.Info, false)]
        /// キャラクターが死亡した時に再生するMMFeedbacks
        [Tooltip("キャラクターが死亡した時に再生するMMFeedbacks")]
        public MMFeedbacks DeathFeedbacks;

        /// これがtrueでない場合、オブジェクトは死亡後もそこに残ります
        [Tooltip("これがtrueでない場合、オブジェクトは死亡後もそこに残ります")]
        public bool DestroyOnDeath = true;

        /// キャラクターが破壊または無効化されるまでの時間（秒）
        [Tooltip("キャラクターが破壊または無効化されるまでの時間（秒）")]
        public float DelayBeforeDestruction = 0f;

        /// trueの場合、キャラクターが死亡した時に衝突判定がオフになります
        [Tooltip("trueの場合、キャラクターが死亡した時に衝突判定がオフになります")]
        public bool CollisionsOffOnDeath = true;

        /// trueの場合、死亡時に重力がオフになります
        [Tooltip("trueの場合、死亡時に重力がオフになります")]
        public bool GravityOffOnDeath = false;

        /// オブジェクトの体力がゼロになった時にプレイヤーが得るポイント
        [Tooltip("オブジェクトの体力がゼロになった時にプレイヤーが得るポイント")]
        public int PointsWhenDestroyed;

        /// falseに設定すると、キャラクターは死亡した場所でリスポーンし、trueの場合は初期位置（シーン開始時）に移動されます
        [Tooltip("falseに設定すると、キャラクターは死亡した場所でリスポーンし、trueの場合は初期位置（シーン開始時）に移動されます")]
        public bool RespawnAtInitialLocation = false;

        [MMInspectorGroup("死亡時の力", true, 10)]

        /// 死亡時に力を適用するかどうか
        [Tooltip("死亡時に力を適用するかどうか")]
        public bool ApplyDeathForce = true;

        /// キャラクターが死亡した時に適用される力
        [Tooltip("キャラクターが死亡した時に適用される力")]
        public Vector2 DeathForce = new Vector2(0, 10);

        /// 死亡時にコントローラーの力を0に設定するかどうか
        [Tooltip("死亡時にコントローラーの力を0に設定するかどうか")]
        public bool ResetForcesOnDeath = false;

        [MMInspectorGroup("共有体力とダメージ耐性", true, 11)]

        /// このHealthが影響を与えるべきCharacter、空の場合は同じゲームオブジェクト上のものを選択します
        [Tooltip("このHealthが影響を与えるべきCharacter、空の場合は同じゲームオブジェクト上のものを選択します")]
        public MyCharacter associatedCharacter;

        /// すべての体力がリダイレクトされる別のHealthコンポーネント（通常は別のキャラクター上）
        /// 例：ボスの複数パーツが同じ体力を共有する場合
        [Tooltip("すべての体力がリダイレクトされる別のHealthコンポーネント（通常は別のキャラクター上）")]
        public MyHealth MasterHealth;

        /// trueの場合、MasterHealthを使用する時、このHealthはダメージを受けず、すべてのダメージがリダイレクトされます。falseの場合、このHealthは自身の体力が消費されると死亡できます
        [Tooltip("trueの場合、MasterHealthを使用する時、このHealthはダメージを受けず、すべてのダメージがリダイレクトされます。falseの場合、このHealthは自身の体力が消費されると死亡できます")]
        public bool OnlyDamageMaster = true;

        /// trueの場合、MasterHealthを使用する時、MasterHealthが死亡すると、このHealthも死亡します
        [Tooltip("trueの場合、MasterHealthを使用する時、MasterHealthが死亡すると、このHealthも死亡します")]
        public bool KillOnMasterHealthDeath = false;



        // プロパティ：外部からアクセス可能な読み取り専用情報
        public float LastDamage { get; set; }
        public Vector3 LastDamageDirection { get; set; }
        public bool Initialized => _initialized;

        /// <summary>
        /// 防御力を取得するプロパティ。
        /// </summary>
        public ElementalStatus DefStatus
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return AIManager.instance.charaDic.GetDefByHash(myHash);
            }
        }

        /// <summary>
        /// 最大HP
        /// </summary>
        public int MaximumHealth
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => AIManager.instance.charaDic.GetBaseInfoByHash(myHash).currentHp;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set => AIManager.instance.charaDic.SetCurrentHPByHash(myHash, value);
        }

        /// <summary>
        /// 現在のHP
        /// </summary>
        public int CurrentHealth
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => AIManager.instance.charaDic.GetBaseInfoByHash(myHash).currentHp;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private set => AIManager.instance.charaDic.SetCurrentHPByHash(myHash, value);
        }

        // デリゲート：イベント処理用
        public delegate void OnHitDelegate();
        public delegate void OnHitZeroDelegate();
        public delegate void OnReviveDelegate();
        public delegate void OnDeathDelegate();

        public OnDeathDelegate OnDeath;
        public OnHitDelegate OnHit;
        public OnHitZeroDelegate OnHitZero;
        public OnReviveDelegate OnRevive;

        // プライベート変数：内部状態管理
        protected CharacterHorizontalMovement _characterHorizontalMovement;
        protected Vector3 _initialPosition;
        protected Color _initialColor;
        protected Renderer _renderer;
        protected MyCharacter _character;
        protected CorgiController _controller;
        protected ProximityManaged _proximityManaged;
        protected MMHealthBar _healthBar;
        protected Collider2D _collider2D;
        protected bool _initialized = false;
        protected AutoRespawn _autoRespawn;
        protected Animator _animator;

        /// <summary>
        /// 死亡エフェクトで使うかも
        /// </summary>
        protected MaterialPropertyBlock _propertyBlock;
        protected bool _hasColorProperty = false;
        protected GameObject _thisObject;
        protected int myHash;

        /// <summary>
        /// 継続ダメージのコルーチン管理用内部クラス
        /// 毒、火傷などの継続的なダメージ効果を管理
        /// </summary>
        protected class InterruptiblesDamageOverTimeCoroutine
        {
            public Coroutine DamageOverTimeCoroutine;
            public DamageType DamageOverTimeType;
        }


        /// <summary>
        /// Awakeで体力を初期化
        /// </summary>
        protected virtual void Start()
        {
            Initialization();
            InitializeSpriteColor();
            InitializeCurrentHealth();
        }

        /// <summary>
        /// 有用なコンポーネントを取得し、ダメージを有効化し、初期色を取得
        /// ゲーム開始時の初期設定を行う重要なメソッド
        /// </summary>
        protected virtual void Initialization()
        {
            myHash = this.gameObject.GetHashCode();
            _character = (AssociatedCharacter == null) ? this.gameObject.GetComponent<Character>() : AssociatedCharacter;

            if ( _character != null )
            {
                _thisObject = _character.gameObject;
                _characterPersistence = _character.FindAbility<CharacterPersistence>();
            }
            else
            {
                _thisObject = this.gameObject;
            }

            // スプライトレンダラーの取得
            if ( this.gameObject.MMGetComponentNoAlloc<SpriteRenderer>() != null )
            {
                _renderer = this.gameObject.GetComponent<SpriteRenderer>();
            }

            // キャラクターモデルのレンダラー取得
            if ( _character != null )
            {
                if ( _character.CharacterModel != null )
                {
                    if ( _character.CharacterModel.GetComponentInChildren<Renderer>() != null )
                    {
                        _renderer = _character.CharacterModel.GetComponentInChildren<Renderer>();
                    }
                }

                // アニメーター取得
                if ( _character.CharacterAnimator != null )
                {
                    _animator = _character.CharacterAnimator;
                }
                else
                {
                    _animator = this.gameObject.GetComponent<Animator>();
                }

                _characterHorizontalMovement = _character.FindAbility<CharacterHorizontalMovement>();
            }
            else
            {
                _animator = this.gameObject.GetComponent<Animator>();
            }

            if ( _animator != null )
            {
                _animator.logWarnings = false;
            }

            // 各種コンポーネントの取得
            _proximityManaged = _thisObject.GetComponentInParent<ProximityManaged>();
            _autoRespawn = _thisObject.GetComponent<AutoRespawn>();
            _controller = _thisObject.GetComponent<CorgiController>();
            _healthBar = _thisObject.GetComponent<MMHealthBar>();
            _collider2D = _thisObject.GetComponent<Collider2D>();

            _propertyBlock = new MaterialPropertyBlock();

            StoreInitialPosition();
            _initialized = true;
            DamageEnabled();
            DisablePostDamageInvulnerability();
            UpdateHealthBar(false);
            if ( _healthBar != null )
            {
                _healthBar.SetInitialActiveState();
            }
        }

        /// <summary>
        /// 体力を初期値または現在値に初期化
        /// MasterHealthが設定されている場合はそちらの値を使用
        /// </summary>
        public virtual void InitializeCurrentHealth()
        {
            if ( (MasterHealth == null) || (!OnlyDamageMaster) )
            {
                SetHealth(InitialHealth, _thisObject);
            }
            else
            {
                if ( MasterHealth.Initialized )
                {
                    SetHealth(MasterHealth.CurrentHealth, _thisObject);
                }
                else
                {
                    SetHealth(MasterHealth.InitialHealth, _thisObject);
                }
            }
        }

        /// <summary>
        /// 初期位置を保存（リスポーン用）
        /// </summary>
        public virtual void StoreInitialPosition()
        {
            _initialPosition = transform.position;
        }

        /// <summary>
        /// このHealthコンポーネントがこのフレームでダメージを受けられるかどうかを返す
        /// ダメージ処理の前段階チェック
        /// </summary>
        /// <returns></returns>
        private virtual bool CanTakeDamageThisFrame()
        {
            // オブジェクトが無敵の場合、何もせずに終了
            // 死亡後は無敵フラグつけていいかもな
            if ( TemporarilyInvulnerable || Invulnerable || ImmuneToDamage || PostDamageInvulnerable || associatedCharacter.CheckMotionFeature(ActionData.ActionFeature.無敵) )
            {
                return false;
            }

            if ( !this.enabled )
            {
                return false;
            }

            // すでにゼロ以下の場合、何もせずに終了
            if ( CurrentHealth <= 0 )
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// オブジェクトがダメージを受けた時に呼び出される
        /// メインのダメージ処理メソッド
        /// </summary>
        /// <param name="damageData">失われる体力ポイントの量</param>
        /// <param name="instigator">ダメージを引き起こしたオブジェクト。これでatkとか取ってくる</param>

        /// <param name="damageDirection">ダメージの方向ベクトル</param>
        public virtual void Damage(DamageStatus damageData, GameObject instigator, bool isAttackRight, in ElementalStatus baseAtk)
        {

            if ( !CanTakeDamageThisFrame() )
            {
                return;
            }

            int hash = instigator.GetHashCode();


            // ガードの確認
            // ガード結果は攻撃者にフィードバックする。
            GuardState guardState = GuardCheck(damageData.CheckHitFeature(AttackHitFeature.パリィ不可), damageData.shock, isAttackRight);

            float damageMultiplier = 1;

            if (  )
            {

            }

            bool isGuard = CheckStun();

            // ダメージ値を計算（耐性込み）
            int damage = ComputeDamageOutput(damageData, baseAtk);

            // 状態変化を処理（毒、麻痺など）
            ComputeCharacterConditionStateChanges(typedDamages);
            ComputeCharacterMovementMultipliers(typedDamages);

            if ( damage <= 0 )
            {
                OnHitZero?.Invoke();
                return;
            }

            // キャラクターの体力をダメージ分減少
            float previousHealth = CurrentHealth;
            if ( MasterHealth != null )
            {
                previousHealth = MasterHealth.CurrentHealth;
                MasterHealth.Damage(damage, instigator, flickerDuration, invincibilityDuration, damageDirection, typedDamages);

                if ( !OnlyDamageMaster )
                {
                    previousHealth = CurrentHealth;
                    SetHealth(CurrentHealth - damage, instigator);
                }
            }
            else
            {
                SetHealth(CurrentHealth - damage, instigator);
            }

            LastDamage = damage;
            LastDamageDirection = damageDirection;
            OnHit?.Invoke();

            if ( CurrentHealth < 0 )
            {
                CurrentHealth = 0;
            }

            // ダメージ後の無敵時間を設定
            if ( (invincibilityDuration > 0) && gameObject.activeInHierarchy )
            {
                EnablePostDamageInvulnerability();
                StartCoroutine(DisablePostDamageInvulnerability(invincibilityDuration));
            }

            // ダメージ受信イベントをトリガー
            MMDamageTakenEvent.Trigger(this, instigator, CurrentHealth, damage, previousHealth);

            if ( _animator != null )
            {
                _animator.SetTrigger("Damage");
            }

            // ダメージフィードバックを再生
            associatedCharacter.OnDamage(damageData, instigator, damageDirection);

            // スプライトの点滅処理
            if ( FlickerSpriteOnHit )
            {
                if ( _renderer != null )
                {
                    StartCoroutine(MMImage.Flicker(_renderer, _initialColor, FlickerColor, 0.05f, flickerDuration));
                }
            }

            // 体力バーを更新
            UpdateHealthBar(true);

            // 体力がゼロになった場合の死亡処理
            if ( MasterHealth != null )
            {
                if ( MasterHealth.CurrentHealth <= 0 )
                {
                    MasterHealth.CurrentHealth = 0;
                    Kill();
                }
                if ( !OnlyDamageMaster )
                {
                    if ( CurrentHealth <= 0 )
                    {
                        CurrentHealth = 0;
                        Kill();
                    }
                }
            }
            else
            {
                if ( CurrentHealth <= 0 )
                {
                    CurrentHealth = 0;
                    Kill();
                }
            }
        }

        /// <summary>
        /// 固定値のダメージを適用
        /// 毒などで使う
        /// </summary>
        /// <param name="damage"></param>
        public virtual void ConstDamage(int damage)
        {

        }

        /// <summary>
        /// ダメージを適用せず、OnHitZeroをトリガーするだけ
        /// 0ダメージの攻撃やブロックされた攻撃で使用
        /// </summary>
        public virtual void DamageZero()
        {
            if ( !gameObject.activeInHierarchy )
            {
                return;
            }
            OnHitZero?.Invoke();
        }

        /// <summary>
        /// キャラクターを殺し、死亡エフェクトをインスタンス化し、ポイントを処理する
        /// 死亡時の全体的な処理を担当
        /// </summary>
        public virtual void Kill()
        {
            if ( ImmuneToDamage )
            {
                return;
            }

            if ( _character != null )
            {
                // 死亡状態をtrueに設定
                _character.ConditionState.ChangeState(CharacterStates.CharacterConditions.Dead);
                _character.Reset();

                if ( _character.CharacterType == Character.CharacterTypes.Player )
                {
                    CorgiEngineEvent.Trigger(CorgiEngineEventTypes.PlayerDeath, _character);
                }
            }
            SetHealth(0f, _thisObject);

            // 以後のダメージを防ぐ
            DamageDisabled();

            // すべての継続ダメージを停止
            StopAllDamageOverTime();

            // 死亡エフェクトをインスタンス化
            DeathFeedbacks?.PlayFeedbacks();

            // 必要に応じてポイントを追加
            if ( PointsWhenDestroyed != 0 )
            {
                CorgiEnginePointsEvent.Trigger(PointsMethods.Add, PointsWhenDestroyed);
            }

            if ( _animator != null )
            {
                _animator.SetTrigger("Death");
            }

            if ( OnDeath != null )
            {
                OnDeath();
            }

            MMLifeCycleEvent.Trigger(this, MMLifeCycleEventTypes.Death);
            HealthDeathEvent.Trigger(this);

            // コントローラーがある場合、衝突を除去し、リスポーン用のパラメータを復元し、死亡力を適用
            if ( _controller != null )
            {
                // 衝突を無視するように設定
                if ( CollisionsOffOnDeath )
                {
                    _controller.CollisionsOff();
                    if ( _collider2D != null )
                    {
                        _collider2D.enabled = false;
                    }
                }

                _controller.ResetParameters();

                if ( GravityOffOnDeath )
                {
                    _controller.GravityActive(false);
                }

                // 必要に応じて死亡時にコントローラーの力をリセット
                if ( ResetForcesOnDeath )
                {
                    _controller.SetForce(Vector2.zero);
                }

                // 死亡力を適用
                if ( ApplyDeathForce )
                {
                    _controller.GravityActive(true);
                    _controller.SetForce(DeathForce);
                }
            }

            // キャラクターがある場合、状態を変更
            if ( _character != null )
            {
                _character.ConditionState.ChangeState(CharacterStates.CharacterConditions.Dead);
                _character.Reset();

                // プレイヤーの場合、ここで終了
                if ( _character.CharacterType == Character.CharacterTypes.Player )
                {
                    return;
                }
            }

            // 破壊前の遅延処理
            if ( DelayBeforeDestruction > 0f )
            {
                Invoke("DestroyObject", DelayBeforeDestruction);
            }
            else
            {
                DestroyObject();
            }
        }

        /// <summary>
        /// このオブジェクトを復活させる
        /// リスポーン処理を担当
        /// </summary>
        public virtual void Revive()
        {
            if ( !_initialized )
            {
                return;
            }

            if ( _characterPersistence != null )
            {
                if ( _characterPersistence.Initialized )
                {
                    return;
                }
            }

            if ( _collider2D != null )
            {
                _collider2D.enabled = true;
            }

            if ( _controller != null )
            {
                _controller.CollisionsOn();
                _controller.GravityActive(true);
                _controller.SetForce(Vector2.zero);
                _controller.ResetParameters();
            }

            if ( _character != null )
            {
                _character.ConditionState.ChangeState(CharacterStates.CharacterConditions.Normal);
            }

            if ( RespawnAtInitialLocation )
            {
                transform.position = _initialPosition;
            }

            Initialization();
            InitializeCurrentHealth();
            if ( FlickerSpriteOnHit && ResetColorOnRevive )
            {
                ResetSpriteColor();
            }

            UpdateHealthBar(false);
            if ( _healthBar != null )
            {
                _healthBar.SetInitialActiveState();
            }
            if ( OnRevive != null )
            {
                OnRevive.Invoke();
            }
            MMLifeCycleEvent.Trigger(this, MMLifeCycleEventTypes.Revive);
        }

        /// <summary>
        /// キャラクターの設定に応じて、オブジェクトを破壊または破壊を試みる
        /// </summary>
        protected virtual void DestroyObject()
        {
            if ( !DestroyOnDeath )
            {
                return;
            }

            if ( _autoRespawn == null )
            {
                // リスポーン時に復元できるように、オブジェクトは非アクティブに変更される
                gameObject.SetActive(false);
            }
            else
            {
                _autoRespawn.Kill();
            }
        }

        /// <summary>
        /// 潜在的な耐性を処理した後、この体力が受けるべきダメージを返す
        /// 耐性システムとの連携で実際のダメージ値を計算
        /// </summary>
        /// <param name="damage">基本ダメージ</param>
        /// <param name="typedDamages">型付きダメージリスト</param>
        /// <param name="damageApplied">ダメージが実際に適用されるかどうか</param>
        /// <returns>計算後のダメージ値とガードが成功したかどうか</returns>
        protected virtual int ComputeDamageOutput(DamageStatus damage, in ElementalStatus atk, ElementalMultiplier atkMultiplier, bool isGuard)
        {

            float totalDamage = 0f;

            int lastElement = BrainStatus.ElementIndices[Element.雷属性];
            float subMotionValue = damage.motionValue * 0.6f;

            // 防御倍率を取得
            ElementalMultiplier defMultiplier = _character.DefMultiplier;

            // ガードじゃない場合
            if ( !isGuard )
            {
                ElementalStatus def = DefStatus;
                for ( int i = 0; i <= lastElement; i++ )
                {
                    Element element = (Element)(1 << i);

                    // メイン属性に含まれている場合
                    if ( (damage.useElement & element) != 0 )
                    {
                        // モーション値を設定
                        // メイン属性に対してはモーション値を使用
                        float motionValue = ((damage.mainElement & element) != 0) ? damage.motionValue : subMotionValue;

                        totalDamage += ((Mathf.Pow(atk[element], 2) * motionValue) / (atk[element] + def[element]))
                            * atkMultiplier[element] * defMultiplier[element];
                    }
                }

                // スタン中はダメージ1.2倍
                if ( _character.ConditionState.CurrentState == MyCharacterStates.CharacterConditions.スタン )
                {
                    totalDamage *= 1.2f;
                }

            }

            // ガードの場合
            else
            {
                (ElementalStatus def, ShieldStatus shield) = AIManager.instance.charaDic.GetDefAndShieldByHash(myHash);

                for ( int i = 0; i <= lastElement; i++ )
                {
                    Element element = (Element)(1 << i);

                    // メイン属性に含まれている場合
                    if ( (damage.useElement & element) != 0 )
                    {
                        // モーション値を設定
                        // メイン属性に対してはモーション値を使用
                        float motionValue = ((damage.mainElement & element) != 0) ? damage.motionValue : subMotionValue;

                        totalDamage += ((Mathf.Pow(atk[element], 2) * motionValue) / (atk[element] + def[element]))
                            * atkMultiplier[element] * defMultiplier[element] * ((float)(100 - shield[element]) * 0.01f);
                    }
                }
            }

            // 最後に全体倍率をかける
            totalDamage = totalDamage * atkMultiplier.allMultiplier * defMultiplier.allMultiplier;

            // ダメージは切り捨てで返す
            return Mathf.CeilToInt(totalDamage);
        }


        /// <summary>
        /// ガード成功したか、パリィできたかを判定する。
        /// 判定したらこれを攻撃側に返す
        /// ガード状態と一緒に
        /// これで弾かれたり
        /// </summary>
        /// <returns>ガード結果</returns>
        protected virtual GuardState GuardCheck(bool isParriable, int shock, bool isAttackerRight)
        {

            // 攻撃の方向とキャラクターの向きから、背後攻撃かどうかを判定
            bool isBackAttack = isAttackerRight != _character.IsFacingRight;

            // ガードが成功したかどうかを判定
            bool isGuard = (isBackAttack && associatedCharacter.CheckMotionFeature(ActionData.ActionFeature.後方ガード))
                || (!isBackAttack && associatedCharacter.CheckMotionFeature(ActionData.ActionFeature.前方ガード));

            // 削り値チェック
            // ちゃんとスタミナを削ろう
            if ( isGuard )
            {
                // 値を返した先でパリィをする
                if ( !isParriable && _character.CheckMotionFeature(ActionData.ActionFeature.パリィする) )
                {
                    return GuardState.パリィ;
                }
                int guardPower = AIManager.instance.charaDic.GetGuardPowerByHash(myHash);

                // ガード力を1.3倍に 
                if ( _character.CheckMotionFeature(ActionData.ActionFeature.ガード強化) )
                {
                    guardPower = (int)(guardPower * 1.3f);

                    // 強化ガード成功時
                    if ( guardPower >= 100 && AIManager.instance.charaDic.UseStamina(myHash, shock * (guardPower / 100)) )
                    {
                        return GuardState.強化ガード;
                    }
                    else
                    {
                        // ガードブレイクスタン処理をここに

                        return GuardState.ガードブレイク;
                    }
                }
                else
                {
                    // ガード成功時
                    if ( guardPower >= 100 && AIManager.instance.charaDic.UseStamina(myHash, shock * (guardPower / 100)) )
                    {
                        return GuardState.ガード成功;
                    }
                    else
                    {
                        // ガードブレイクスタン処理をここに

                        return GuardState.ガードブレイク;
                    }
                }
            }

            return GuardState.ガードしない;
        }

        /// <summary>
        /// 耐性を通して処理することで新しいノックバック力を決定
        /// </summary>
        /// <param name="knockbackForce">基本ノックバック力</param>
        /// <param name="typedDamages">型付きダメージリスト</param>
        /// <returns>計算後のノックバック力</returns>
        public virtual void StunCheck(Vector2 knockbackForce, int shock, bool isAttackerRight)
        {

            // 攻撃の方向とキャラクターの向きから、背後攻撃かどうかを判定
            bool isBackAttack = isAttackerRight != _character.IsFacingRight;

            // ガードが成功したかどうかを判定
            bool isGuard = (isBackAttack && associatedCharacter.CheckMotionFeature(ActionData.ActionFeature.後方ガード))
                || (!isBackAttack && associatedCharacter.CheckMotionFeature(ActionData.ActionFeature.前方ガード));

            // 削り値チェック
            // ちゃんとスタミナを削ろう
            if ( isGuard )
            {
                int guardPower = AIManager.instance.charaDic.GetGuardPowerByHash(myHash);

            }

            // 
            else
            {

            }

            return;
        }

        /// <summary>
        /// このHealthがノックバックを受けられる場合はtrue、そうでなければfalseを返す
        /// </summary>
        /// <param name="typedDamages">型付きダメージリスト</param>
        /// <returns>ノックバック可能かどうか</returns>
        public virtual bool CanGetKnockback(List<TypedDamage> typedDamages)
        {
            if ( ImmuneToKnockback )
            {
                return false;
            }
            if ( TargetDamageResistanceProcessor != null )
            {
                if ( TargetDamageResistanceProcessor.isActiveAndEnabled )
                {
                    bool checkResistance = TargetDamageResistanceProcessor.CheckPreventKnockback(typedDamages);
                    if ( checkResistance )
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// 耐性を通して、必要に応じて状態変化を適用
        /// 例：毒状態、麻痺状態など
        /// </summary>
        /// <param name="typedDamages">型付きダメージリスト</param>
        protected virtual void ComputeCharacterConditionStateChanges(List<TypedDamage> typedDamages)
        {
            if ( (typedDamages == null) || (_character == null) )
            {
                return;
            }

            foreach ( TypedDamage typedDamage in typedDamages )
            {
                if ( typedDamage.ForceCharacterCondition )
                {
                    if ( TargetDamageResistanceProcessor != null )
                    {
                        if ( TargetDamageResistanceProcessor.isActiveAndEnabled )
                        {
                            bool checkResistance =
                                TargetDamageResistanceProcessor.CheckPreventCharacterConditionChange(typedDamage.AssociatedDamageType);
                            if ( checkResistance )
                            {
                                continue;
                            }
                        }
                    }
                    _character.ChangeCharacterConditionTemporarily(typedDamage.ForcedCondition, typedDamage.ForcedConditionDuration, typedDamage.ResetControllerForces, typedDamage.DisableGravity);
                }
            }
        }

        /// <summary>
        /// 耐性リストを通して、必要に応じて移動倍率を適用
        /// 例：氷攻撃による移動速度低下
        /// </summary>
        /// <param name="typedDamages">型付きダメージリスト</param>
        protected virtual void ComputeCharacterMovementMultipliers(List<TypedDamage> typedDamages)
        {
            if ( (typedDamages == null) || (_character == null) )
            {
                return;
            }

            foreach ( TypedDamage typedDamage in typedDamages )
            {
                if ( typedDamage.ApplyMovementMultiplier )
                {
                    if ( TargetDamageResistanceProcessor != null )
                    {
                        if ( TargetDamageResistanceProcessor.isActiveAndEnabled )
                        {
                            bool checkResistance =
                                TargetDamageResistanceProcessor.CheckPreventMovementModifier(typedDamage.AssociatedDamageType);
                            if ( checkResistance )
                            {
                                continue;
                            }
                        }
                    }

                    _characterHorizontalMovement?.ApplyContextSpeedMultiplier(typedDamage.MovementMultiplier, typedDamage.MovementMultiplierDuration);
                }
            }
        }

        /// <summary>
        /// キャラクターが体力を得る時に呼び出される（例：回復アイテムから）
        /// </summary>
        /// <param name="health">キャラクターが得る体力</param>
        /// <param name="instigator">キャラクターに体力を与えるもの</param>
        public virtual void GetHealth(int health, GameObject instigator)
        {
            // この関数はキャラクターの体力に体力を追加し、MaxHealthを上回らないようにします
            if ( MasterHealth != null )
            {
                MasterHealth.SetHealth(Mathf.Min(CurrentHealth + health, MaximumHealth), instigator);
            }
            else
            {
                SetHealth(Mathf.Min(CurrentHealth + health, MaximumHealth), instigator);
            }
            UpdateHealthBar(true);
        }

        /// <summary>
        /// キャラクターの体力をパラメータで指定されたものに設定
        /// </summary>
        /// <param name="newHealth">新しい体力値</param>
        /// <param name="instigator">変更の原因</param>
        public virtual void SetHealth(int newHealth, GameObject instigator)
        {
            CurrentHealth = Mathf.Min(newHealth, MaximumHealth);
            UpdateHealthBar(false);
            HealthChangeEvent.Trigger(this, newHealth);
        }

        /// <summary>
        /// キャラクターの体力を最大値にリセット
        /// </summary>
        public virtual void ResetHealthToMaxHealth()
        {
            CurrentHealth = MaximumHealth;
            UpdateHealthBar(false);
            HealthChangeEvent.Trigger(this, CurrentHealth);
        }

        /// <summary>
        /// キャラクターの体力バーの進行状況を更新
        /// </summary>
        /// <param name="show">体力バーを表示するかどうか</param>
        public virtual void UpdateHealthBar(bool show)
        {
            if ( _healthBar != null )
            {
                _healthBar.UpdateBar(CurrentHealth, 0f, MaximumHealth, show);
            }

            if ( _character != null )
            {
                if ( _character.CharacterType == Character.CharacterTypes.Player )
                {
                    // 体力バーを更新
                    if ( GUIManager.HasInstance )
                    {
                        GUIManager.Instance.UpdateHealthBar(CurrentHealth, 0f, MaximumHealth, _character.PlayerID);
                    }
                }
            }
        }

        #region ノックバック

        protected virtual void ApplyDamageCausedKnockback(float damage, List<TypedDamage> typedDamages)
        {
            if ( !ShouldApplyKnockback(damage, typedDamages) )
            {
                return;
            }

            _knockbackForce.x = DamageCausedKnockbackForce.x;
            switch ( DamageCausedKnockbackDirection )
            {
                case CausedKnockbackDirections.BasedOnOwnerPosition:
                    if ( Owner == null )
                    { Owner = this.gameObject; }
                    Vector2 relativePosition = _colliderCorgiController. - Owner.transform.position;
                    _knockbackForce.x *= Mathf.Sign(relativePosition.x);
                    break;
                case CausedKnockbackDirections.BasedOnSpeed:
                    Vector2 totalVelocity = _colliderCorgiController.Speed + _velocity;
                    _knockbackForce.x *= -1 * Mathf.Sign(totalVelocity.x);
                    break;
                case CausedKnockbackDirections.BasedOnDamageOnTouchPosition:
                    Vector3 _colliderOffset =
                        (_boxCollider2D != null) ? _boxCollider2D.offset : _circleCollider2D.offset;
                    _knockbackForce.x *= Mathf.Sign((_colliderCorgiController.transform.position - (this.gameObject.transform.position + _colliderOffset)).x);
                    break;
            }

            _knockbackForce.y = DamageCausedKnockbackForce.y;

            _knockbackForce = _colliderHealth.ComputeKnockbackForce(_knockbackForce, typedDamages);

            switch ( DamageCausedKnockbackType )
            {
                case KnockbackStyles.SetForce:
                    _colliderCorgiController.SetForce(_knockbackForce);
                    _characterJump = _colliderCorgiController.gameObject.MMGetComponentNoAlloc<Character>()?.FindAbility<CharacterJump>();
                    if ( _characterJump != null )
                    {
                        _characterJump.SetCanJumpStop(false);
                        _characterJump.SetJumpFlags();
                    }
                    break;
                case KnockbackStyles.AddForce:
                    _colliderCorgiController.AddForce(_knockbackForce);
                    _characterJump = _colliderCorgiController.gameObject.MMGetComponentNoAlloc<Character>()?.FindAbility<CharacterJump>();
                    if ( _characterJump != null )
                    {
                        _characterJump.SetCanJumpStop(false);
                        _characterJump.SetJumpFlags();
                    }
                    break;
            }
        }

        /// <summary>
        /// ノックバックを適用すべきかどうかを決定
        /// 
        /// ノックバック関連はヘルスに移行するか。
        /// damagestatusに持たせておいて
        /// </summary>
        /// <returns></returns>
        protected virtual bool ShouldApplyKnockback(float damage, List<TypedDamage> typedDamages)
        {

            return (_colliderCorgiController != null)
                   && (DamageCausedKnockbackType != KnockbackStyles.NoKnockback)
                   && (DamageCausedKnockbackForce != Vector2.zero)
                   && !_colliderHealth.Invulnerable
                   && !_colliderHealth.PostDamageInvulnerable
                   && _colliderHealth.CanGetKnockback(typedDamages);
        }


        #endregion

        #region 外部からの設定変更

        /// <summary>
        /// キャラクターがダメージを受けることを防ぐ
        /// </summary>
        public virtual void DamageDisabled()
        {
            TemporarilyInvulnerable = true;
        }

        /// <summary>
        /// キャラクターがダメージを受けることを許可
        /// </summary>
        public virtual void DamageEnabled()
        {
            TemporarilyInvulnerable = false;
        }

        /// <summary>
        /// ダメージ後無敵状態を有効化
        /// </summary>
        public virtual void EnablePostDamageInvulnerability()
        {
            PostDamageInvulnerable = true;
        }

        /// <summary>
        /// ダメージ後無敵状態を無効化
        /// </summary>
        public virtual void DisablePostDamageInvulnerability()
        {
            PostDamageInvulnerable = false;
        }

        /// <summary>
        /// 遅延後にダメージ後無敵状態を無効化
        /// </summary>
        public virtual IEnumerator DisablePostDamageInvulnerability(float delay)
        {
            yield return MMCoroutine.WaitFor(delay);
            PostDamageInvulnerable = false;
        }

        /// <summary>
        /// 指定された遅延後にキャラクターが再びダメージを受けられるようにします
        /// </summary>
        public virtual IEnumerator DamageEnabled(float delay)
        {
            yield return MMCoroutine.WaitFor(delay);
            TemporarilyInvulnerable = false;
        }

        #endregion

        /// <summary>
        /// オブジェクトが有効化された時（例：リスポーン時）、初期体力レベルを復元
        /// </summary>
        protected virtual void OnEnable()
        {
            if ( (_characterPersistence != null) && (_characterPersistence.Initialized) )
            {
                UpdateHealthBar(false);
                return;
            }

            this.MMEventStartListening<HealthDeathEvent>();

            if ( (_proximityManaged != null) && _proximityManaged.StateChangedThisFrame )
            {
                return;
            }
            InitializeCurrentHealth();
            DamageEnabled();
            DisablePostDamageInvulnerability();
            UpdateHealthBar(false);
        }

        /// <summary>
        /// 無効化時に実行中のすべてのInvokeをキャンセル
        /// </summary>
        protected virtual void OnDisable()
        {
            CancelInvoke();
            this.MMEventStopListening<HealthDeathEvent>();
        }

        /// <summary>
        /// HealthDeathEventの処理
        /// MasterHealthが死亡した場合の処理
        /// </summary>
        public void OnMMEvent(HealthDeathEvent deathEvent)
        {
            if ( KillOnMasterHealthDeath && (deathEvent.AffectedHealth == MasterHealth) )
            {
                Kill();
            }
        }
    }

}