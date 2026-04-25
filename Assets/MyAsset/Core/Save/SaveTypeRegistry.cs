using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// SaveDataStore.ResolveType の許可リスト型解決基盤。
    ///
    /// なぜ必要か:
    /// 旧 ResolveType は全アセンブリから任意 typeName を Assembly.GetType で検索していたため、
    /// save ファイルを偽造されれば任意型のインスタンス化経路が作れた
    /// (Newtonsoft.Json TypeNameHandling.All 相当のリスク)。
    /// SaveTypeRegistry は明示的に登録された型のみ TryResolve で返すことで、未許可型を
    /// JToken フォールバックに乗せ、危険型のインスタンス化を遮断する。
    ///
    /// 後方互換:
    /// 既存セーブファイルが ISaveable.Serialize() の戻り値として保存している primitive/BCL 型
    /// (int, string, int[], Dictionary&lt;string,bool&gt; 等) は PrePopulatePrimitives で
    /// 初期登録される。process 起動時に <see cref="AutoRegisterAllSaveables"/> が
    /// AppDomain から ISaveable 実装型も自動登録するため、明示登録漏れによる JToken 化を防ぐ。
    ///
    /// テスト時のリセット:
    /// process-wide static state を持つため、テストでは <see cref="Reset"/> でクリアし、
    /// <see cref="PrePopulatePrimitives"/> または <see cref="RegisterType"/> で必要分のみ登録する。
    /// </summary>
    public static class SaveTypeRegistry
    {
        // 許可型の集合。FullName が key (Type.GetType / SaveDataStore の typeName と整合させる)
        private static readonly Dictionary<string, Type> _allowedByName = new Dictionary<string, Type>();
        private static readonly HashSet<Type> _allowedTypes = new HashSet<Type>();

        /// <summary>
        /// プロセス起動時に自動的に primitive 型と ISaveable 実装型を allowlist に登録する。
        /// SubsystemRegistration で domain reload 直後に走らせ、最初の Awake 前に完了させる。
        /// テスト時は Reset / PrePopulatePrimitives で個別に状態を制御する。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            PrePopulatePrimitives();
            AutoRegisterAllSaveables();
        }

        /// <summary>
        /// 型を許可リストに登録する。
        /// 既存登録があれば何もしない (idempotent)。
        /// </summary>
        public static void RegisterType(Type type)
        {
            if (type == null)
            {
                return;
            }

            string fullName = type.FullName;
            if (string.IsNullOrEmpty(fullName))
            {
                return;
            }

            _allowedByName[fullName] = type;
            _allowedTypes.Add(type);
        }

        /// <summary>
        /// ジェネリック糖衣。<see cref="RegisterType(Type)"/> と等価。
        /// </summary>
        public static void RegisterType<T>()
        {
            RegisterType(typeof(T));
        }

        /// <summary>
        /// 型が許可リストに含まれているかを返す。
        /// </summary>
        public static bool IsAllowed(Type type)
        {
            if (type == null)
            {
                return false;
            }
            return _allowedTypes.Contains(type);
        }

        /// <summary>
        /// typeName から許可済み型を取得する。
        /// 未登録 / null / empty の場合は false を返し、呼び出し側 (SaveDataStore.ResolveType) で
        /// JToken フォールバックに乗せる。
        /// </summary>
        public static bool TryResolve(string typeName, out Type type)
        {
            type = null;
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return false;
            }

            return _allowedByName.TryGetValue(typeName, out type);
        }

        /// <summary>
        /// 既存セーブファイルが ISaveable.Serialize() の戻り値として保存する primitive / BCL 型を
        /// 一括登録する。型一覧は SisterGame の現実装で実際に Serialize() が返している型に基づく
        /// (CurrencyManager → int、InventoryManager → List&lt;ItemEntry&gt;、
        ///  GateRegistry → Dictionary&lt;string,bool&gt; 等)。
        /// 個別の Game.Core 構造体型は <see cref="AutoRegisterAllSaveables"/> 経由ではなく
        /// 関連 ISaveable 型と一緒に手動登録するか、必要時に呼び出し側で RegisterType することを想定。
        /// </summary>
        public static void PrePopulatePrimitives()
        {
            // 値型 primitives
            RegisterType<int>();
            RegisterType<long>();
            RegisterType<short>();
            RegisterType<byte>();
            RegisterType<sbyte>();
            RegisterType<uint>();
            RegisterType<ulong>();
            RegisterType<ushort>();
            RegisterType<float>();
            RegisterType<double>();
            RegisterType<decimal>();
            RegisterType<bool>();
            RegisterType<char>();
            RegisterType<string>();

            // 配列形
            RegisterType<int[]>();
            RegisterType<long[]>();
            RegisterType<float[]>();
            RegisterType<double[]>();
            RegisterType<bool[]>();
            RegisterType<string[]>();

            // 既存実装で利用される List / Dictionary 形 (ChallengeManager / GateRegistry 等)
            RegisterType<List<string>>();
            RegisterType<List<int>>();
            RegisterType<List<bool>>();
            RegisterType<Dictionary<string, bool>>();
            RegisterType<Dictionary<string, int>>();
            RegisterType<Dictionary<string, string>>();
            RegisterType<Dictionary<string, Dictionary<string, bool>>>();
        }

        /// <summary>
        /// AppDomain から ISaveable 実装型を全て検出して allowlist に登録する。
        /// 抽象クラス / インターフェース / generic 定義型は除外する (具象型のみ)。
        /// 例外を投げるアセンブリは個別 skip し、全体は継続する。
        ///
        /// 加えて、各 ISaveable 実装型の <see cref="ISaveable.Serialize"/> メソッド宣言の
        /// 戻り値型 (interface 上は object なので実体は overload された Serialize の戻り値) は
        /// runtime に判別できないため、ISaveable 型と共によく使われる payload 型 (たとえば
        /// AITemplateData / LeaderboardEntry / FlagSaveData / LevelUpSaveData / ItemEntry) も
        /// 同じ assembly 内の `[Serializable]` struct/class からまとめて登録する。
        /// これにより既存セーブファイルの後方互換性 (List&lt;CustomType&gt; / CustomType[] 等の復元) を維持する。
        /// </summary>
        public static void AutoRegisterAllSaveables()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type[] types;
                try
                {
                    types = assemblies[i].GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    // Unity Editor では一部 dll が部分ロード状態になることがある。
                    // 取得できた型のみ扱い、null は除外して continue する
                    types = ex.Types;
                }
                catch (Exception)
                {
                    continue;
                }

                if (types == null)
                {
                    continue;
                }

                bool hasSaveable = false;
                for (int j = 0; j < types.Length; j++)
                {
                    Type t = types[j];
                    if (t == null)
                    {
                        continue;
                    }
                    if (t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition)
                    {
                        continue;
                    }
                    if (typeof(ISaveable).IsAssignableFrom(t))
                    {
                        RegisterType(t);
                        hasSaveable = true;
                    }
                }

                // ISaveable 実装を含む assembly については、同 assembly 内の [Serializable] DTO 型
                // (Serialize() の payload として実際に使われる data container) も併せて登録する。
                // ISaveable を含まない assembly はゲーム外の dll なので走査しない (登録漏れリスクより
                // 安全側に倒す)。
                if (!hasSaveable)
                {
                    continue;
                }

                for (int j = 0; j < types.Length; j++)
                {
                    Type t = types[j];
                    if (t == null)
                    {
                        continue;
                    }
                    if (t.IsAbstract || t.IsInterface || t.IsGenericTypeDefinition)
                    {
                        continue;
                    }
                    // Unity の SerializableAttribute / .NET の SerializableAttribute は同じ。
                    // [Serializable] が付いた DTO のみ (任意型を全部許可するわけではない)。
                    object[] attrs = t.GetCustomAttributes(typeof(SerializableAttribute), false);
                    if (attrs == null || attrs.Length == 0)
                    {
                        continue;
                    }
                    RegisterType(t);

                    // 配列形 / List<T> 形も同時に登録 (既存 ISaveable は配列や List<DTO> を Serialize で返すケースが多い)
                    try
                    {
                        RegisterType(t.MakeArrayType());
                        RegisterType(typeof(List<>).MakeGenericType(t));
                    }
                    catch (Exception)
                    {
                        // open-generic 制約で MakeGenericType / MakeArrayType が失敗するケースは諦める
                    }
                }
            }
        }

        /// <summary>
        /// 許可リストを完全クリアする。テスト用途のみ想定。
        /// プロダクションコードからは呼び出さないこと (process-wide state を破壊するため)。
        /// </summary>
        public static void Reset()
        {
            _allowedByName.Clear();
            _allowedTypes.Clear();
        }
    }
}
