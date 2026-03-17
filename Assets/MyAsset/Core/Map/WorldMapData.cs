using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// ワールドマップのデータ管理。訪問済みエリア記録、探索率計算、エリア境界判定。
    /// MonoBehaviour非依存のピュアロジック。
    /// </summary>
    public class WorldMapData
    {
        private List<AreaDefinition> _areas;
        private HashSet<string> _visitedAreaIds;

        public int TotalAreaCount => _areas.Count;
        public int VisitedAreaCount => _visitedAreaIds.Count;

        public WorldMapData()
        {
            _areas = new List<AreaDefinition>();
            _visitedAreaIds = new HashSet<string>();
        }

        /// <summary>エリア定義を追加する</summary>
        public void AddArea(AreaDefinition area)
        {
            _areas.Add(area);
        }

        /// <summary>エリアを訪問済みにする。既に訪問済みならfalse。</summary>
        public bool MarkVisited(string areaId)
        {
            return _visitedAreaIds.Add(areaId);
        }

        /// <summary>エリアが訪問済みか</summary>
        public bool IsVisited(string areaId)
        {
            return _visitedAreaIds.Contains(areaId);
        }

        /// <summary>探索率を0.0〜1.0で返す。エリア0個なら0。</summary>
        public float GetExplorationRate()
        {
            if (_areas.Count == 0)
            {
                return 0f;
            }

            return (float)_visitedAreaIds.Count / _areas.Count;
        }

        /// <summary>指定座標が含まれるエリアのIDを返す。該当なしならnull。</summary>
        public string GetAreaAtPosition(Vector2 position)
        {
            for (int i = 0; i < _areas.Count; i++)
            {
                if (_areas[i].bounds.Contains(position))
                {
                    return _areas[i].areaId;
                }
            }

            return null;
        }

        /// <summary>永続化用: 訪問済みエリアIDリストを取得</summary>
        public List<string> GetVisitedAreaIds()
        {
            return new List<string>(_visitedAreaIds);
        }

        /// <summary>永続化用: 訪問済みエリアIDリストを復元</summary>
        public void RestoreVisitedAreas(List<string> areaIds)
        {
            _visitedAreaIds.Clear();
            for (int i = 0; i < areaIds.Count; i++)
            {
                _visitedAreaIds.Add(areaIds[i]);
            }
        }
    }
}
