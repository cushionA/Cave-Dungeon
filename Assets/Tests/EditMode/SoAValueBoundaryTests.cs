using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Core;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    /// <summary>
    /// ContainerSetting.structType の行データが、ネストした構造体も含めて
    /// managed参照を持たないことを検査する。Native APIやblittabilityの実行検証とは別。
    /// ManagedCharacterのsidecarと、攻撃packet・作者SOは対象に含めない。
    /// </summary>
    public class SoAValueBoundaryTests
    {
        [TestCase(typeof(CharacterVitals))]
        [TestCase(typeof(CombatStats))]
        [TestCase(typeof(CharacterFlags))]
        [TestCase(typeof(MoveParams))]
        [TestCase(typeof(EquipmentStatus))]
        [TestCase(typeof(CharacterStatusEffects))]
        [TestCase(typeof(AnimationStateData))]
        [TestCase(typeof(CharacterDefenseState))]
        [TestCase(typeof(CharacterBaseStats))]
        [TestCase(typeof(CharacterGrowth))]
        public void SoAValueBoundary_RowData_ContainsNoManagedReferences(Type rowType)
        {
            List<string> references = new List<string>();
            CollectManagedReferences(rowType, rowType.Name, references, new HashSet<Type>());

            Assert.IsTrue(rowType.IsValueType, "A native row must be a value type.");
            Assert.IsEmpty(references, string.Join(Environment.NewLine, references));
        }

        private static void CollectManagedReferences(Type type, string path,
            List<string> references, HashSet<Type> visited)
        {
            if (!type.IsValueType)
            {
                references.Add(path + ": " + type.FullName);
                return;
            }
            if (type.IsPrimitive || type.IsEnum || !visited.Add(type)) { return; }
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                CollectManagedReferences(field.FieldType, path + "." + field.Name, references, visited);
            }
        }
    }
}
