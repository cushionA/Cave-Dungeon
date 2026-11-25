using System;
using System.Globalization;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Core
{
    /// <summary>正式に開始した周回番号をセーブスロットごとに保持する。</summary>
    public sealed class ExpeditionPlaythrough : ISaveableWithMissingEntry, ISaveableWithLoadRollback, ISaveableLoadPreflight
    {
        private const int k_FirstCycle = 1;
        private CycleState _cycle = new CycleState(k_FirstCycle);

        public string SaveId => "ExpeditionPlaythrough";
        public int CycleNumber => CurrentCycle.Number;
        internal CycleState CurrentCycle => Volatile.Read(ref _cycle);

        /// <summary>周回開始を通知した側が持つ周回番号と一致するときだけ一度進める。</summary>
        public bool TryBeginNextPlaythrough(int expectedCycle)
        {
            CycleState current = CurrentCycle;
            return current.Number == expectedCycle && expectedCycle < int.MaxValue
                && ReferenceEquals(Interlocked.CompareExchange(ref _cycle, new CycleState(expectedCycle + 1), current), current);
        }

        object ISaveable.Serialize() => CycleNumber;

        void ISaveable.Deserialize(object data) => Volatile.Write(ref _cycle, new CycleState(RequireCycle(data)));

        void ISaveableLoadPreflight.ValidateLoad(object data) => RequireCycle(data);

        object ISaveableWithLoadRollback.CaptureLoadRollback() => CurrentCycle;

        void ISaveableWithLoadRollback.RestoreLoadRollback(object snapshot) => Volatile.Write(ref _cycle, (CycleState)snapshot);

        private static int RequireCycle(object data) => TryReadCycle(data, out int cycle)
            ? cycle : throw new FormatException("ExpeditionPlaythrough の周回番号が無効です。");

        internal static bool TryReadCycle(object data, out int cycle)
        {
            if (data is JValue value && value.Type == JTokenType.Integer)
            {
                return int.TryParse(value.ToString(Formatting.None), NumberStyles.None, CultureInfo.InvariantCulture, out cycle)
                    && cycle >= k_FirstCycle;
            }
            cycle = data switch
            {
                int number when number >= k_FirstCycle => number,
                long number when number >= k_FirstCycle && number <= int.MaxValue => (int)number,
                _ => 0,
            };
            return cycle >= k_FirstCycle;
        }

        void ISaveableWithMissingEntry.RestoreMissingEntry() => Volatile.Write(ref _cycle, new CycleState(k_FirstCycle));

        internal sealed class CycleState
        {
            public int Number { get; }

            public CycleState(int number) => Number = number;
        }
    }
}
