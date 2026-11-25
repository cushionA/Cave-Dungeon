using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Runtime
{
    /// <summary>遭遇の目撃に対し、既存の歩行モーターで到達できる射線付きの接近地点を選ぶ。</summary>
    internal sealed class EncounterApproachRoutes3D
    {
        internal const int k_MaxWaypoints = 12;
        private const float k_MaxSegment = 12f;
        private const float k_FloorTolerance = 0.2f;
        private const float k_FloorProbeSpacing = 0.3f;
        private const float k_WaypointArrival = 0.12f;
        private const float k_TargetDrift = 0.35f;
        private const float k_RetryDelay = 0.25f;
        private const float k_StallTime = 0.5f;

        private sealed class Route
        {
            public readonly Vector3[] steps = new Vector3[k_MaxWaypoints + 1];
            public ActorRef target;
            public Vector3 targetPosition;
            public int next = -1;
            public float retryAt;
            public float stalledTime;
            public float bestDistance;

            public void ResetProgress()
            {
                stalledTime = 0f;
                bestDistance = float.PositiveInfinity;
            }
        }

        private readonly Dictionary<ActorRef, Route> _routes = new();
        private readonly List<ActorRef> _retired = new();
        private readonly Vector3[] _nodes = new Vector3[k_MaxWaypoints + 1];
        private readonly float[] _costs = new float[k_MaxWaypoints + 1];
        private readonly int[] _previous = new int[k_MaxWaypoints + 1];
        private readonly bool[] _settled = new bool[k_MaxWaypoints + 1];
        private readonly Collider[] _overlaps = new Collider[8];

        internal void Remove(ActorRef owner) => _routes.Remove(owner);

        internal void Clear() => _routes.Clear();

        internal void Retain(List<ActorRef> participants)
        {
            foreach (ActorRef owner in _routes.Keys)
            {
                if (!participants.Contains(owner)) { _retired.Add(owner); }
            }
            foreach (ActorRef owner in _retired) { _routes.Remove(owner); }
            _retired.Clear();
        }

        internal Vector3 GetDirection(CharacterActor3D actor, in EncounterSighting3D sighting, float attackRange,
            Transform[] waypoints, LayerMask groundLayers, LayerMask blockers)
        {
            Scene scene = actor.gameObject.scene;
            if (!actor.Motor.IsGrounded || !(attackRange > 0f) || !float.IsFinite(attackRange)
                || !MultiLockTargeting3D.TryGetActor(sighting.Target, out CharacterActor3D target)
                || target.gameObject.scene != scene)
            {
                Remove(actor.Actor);
                return Vector3.zero;
            }

            if (!_routes.TryGetValue(actor.Actor, out Route route))
            {
                route = new Route();
                _routes.Add(actor.Actor, route);
            }
            if (route.target != sighting.Target || (route.targetPosition - sighting.Position).sqrMagnitude > k_TargetDrift * k_TargetDrift)
            {
                route.target = sighting.Target;
                route.targetPosition = sighting.Position;
                route.next = -1;
                route.retryAt = 0f;
            }

            Vector3 feet = actor.Motor.FeetPosition;
            PhysicsScene world = scene.GetPhysicsScene();
            Bounds body = actor.Body.bounds;
            float radius = Mathf.Min(body.extents.x, body.extents.z);
            float height = body.size.y;
            if (!world.IsValid() || !(radius > 0f) || !(height > 0f)) { return Vector3.zero; }

            while (route.next >= 0 && (route.steps[route.next] - feet).sqrMagnitude <= k_WaypointArrival * k_WaypointArrival)
            {
                route.next--;
                route.ResetProgress();
            }
            if (route.next >= 0)
            {
                float distance = (route.steps[route.next] - feet).sqrMagnitude;
                if (distance < route.bestDistance - 0.0025f) { route.ResetProgress(); route.bestDistance = distance; }
                else { route.stalledTime += Time.fixedDeltaTime; }
                if (route.stalledTime >= k_StallTime || !CanWalk(world, feet, route.steps[route.next], radius, height, groundLayers, blockers))
                {
                    route.next = -1;
                    route.retryAt = 0f;
                }
            }
            if (route.next < 0 && Time.fixedTime >= route.retryAt)
            {
                route.retryAt = Time.fixedTime + k_RetryDelay;
                route.ResetProgress();
                FindRoute(route, scene, world, actor.Body, target.Body, feet, sighting, attackRange, waypoints, groundLayers, blockers);
            }
            if (route.next < 0) { return Vector3.zero; }
            Vector3 direction = route.steps[route.next] - feet;
            direction.y = 0f;
            return direction.normalized;
        }

        private void FindRoute(Route route, Scene scene, PhysicsScene world, CharacterController body, CharacterController target,
            Vector3 feet, in EncounterSighting3D sighting, float attackRange, Transform[] waypoints, LayerMask groundLayers, LayerMask blockers)
        {
            _nodes[0] = feet;
            int count = 1;
            for (int i = 0; i < Mathf.Min(waypoints.Length, k_MaxWaypoints); i++)
            {
                Transform marker = waypoints[i];
                if (marker != null && marker.gameObject.activeInHierarchy && marker.gameObject.scene == scene
                    && TryGetGround(world, marker.position, groundLayers, out Vector3 point))
                {
                    _nodes[count++] = point;
                }
            }
            for (int i = 0; i < count; i++)
            {
                _costs[i] = float.PositiveInfinity;
                _previous[i] = -1;
                _settled[i] = false;
            }
            _costs[0] = 0f;
            Bounds bounds = body.bounds;
            float radius = Mathf.Min(bounds.extents.x, bounds.extents.z);
            float height = bounds.size.y;
            Vector3 aimOffset = bounds.center - feet;
            int goalNode = -1;
            float goalCost = float.PositiveInfinity;
            Vector3 goal = default;
            for (int i = 0; i < count; i++)
            {
                int current = -1;
                float currentCost = goalCost;
                for (int candidate = 0; candidate < count; candidate++)
                {
                    if (_settled[candidate] || _costs[candidate] >= currentCost) { continue; }
                    current = candidate;
                    currentCost = _costs[candidate];
                }
                if (current < 0) { break; }
                _settled[current] = true;
                Vector3 start = _nodes[current];

                Vector3 targetOffset = start - sighting.Position;
                targetOffset.y = 0f;
                Vector3 proposed = sighting.Position + Vector3.ClampMagnitude(targetOffset, attackRange * 0.75f);
                if (TryGetGround(world, proposed, groundLayers, out Vector3 landing)
                    && (landing - sighting.Position).sqrMagnitude <= attackRange * attackRange
                    && currentCost + Vector3.Distance(start, landing) < goalCost
                    && CanWalk(world, start, landing, radius, height, groundLayers, blockers)
                    && IsClearAt(world, landing, radius, height, blockers, body)
                    && HasSight(world, landing + aimOffset, sighting.TargetCenter, blockers, target))
                {
                    goalNode = current;
                    goalCost = currentCost + Vector3.Distance(start, landing);
                    goal = landing;
                }

                for (int neighbor = 1; neighbor < count; neighbor++)
                {
                    if (_settled[neighbor]) { continue; }
                    Vector3 end = _nodes[neighbor];
                    float cost = currentCost + Vector3.Distance(start, end);
                    if (cost >= _costs[neighbor] || cost >= goalCost
                        || !CanWalk(world, start, end, radius, height, groundLayers, blockers)) { continue; }
                    _costs[neighbor] = cost;
                    _previous[neighbor] = current;
                }
            }

            if (goalNode < 0) { return; }
            route.steps[++route.next] = goal;
            for (int node = goalNode; node > 0; node = _previous[node]) { route.steps[++route.next] = _nodes[node]; }
        }

        private static bool CanWalk(PhysicsScene world, Vector3 start, Vector3 end, float radius, float height,
            LayerMask groundLayers, LayerMask blockers)
        {
            Vector3 offset = end - start;
            float length = offset.magnitude;
            if (!float.IsFinite(length) || length > k_MaxSegment || Mathf.Abs(offset.y) > k_FloorTolerance) { return false; }
            if (length <= 0.001f) { return true; }

            Capsule(start, radius, height, out Vector3 bottom, out Vector3 top);
            if (world.CapsuleCast(bottom, top, radius, offset / length, out _, length, blockers, QueryTriggerInteraction.Ignore)) { return false; }
            int samples = Mathf.CeilToInt(length / k_FloorProbeSpacing);
            for (int i = 1; i <= samples; i++)
            {
                if (!TryGetGround(world, Vector3.Lerp(start, end, (float)i / samples), groundLayers, out _)) { return false; }
            }
            return true;
        }

        private static bool TryGetGround(PhysicsScene world, Vector3 point, LayerMask groundLayers, out Vector3 ground)
        {
            ground = default;
            if (!float.IsFinite(point.sqrMagnitude) || !world.Raycast(point + Vector3.up * k_FloorTolerance, Vector3.down,
                out RaycastHit hit, 2f * k_FloorTolerance, groundLayers, QueryTriggerInteraction.Ignore) || hit.normal.y < 0.9f)
            {
                return false;
            }
            ground = hit.point;
            return true;
        }

        private bool IsClearAt(PhysicsScene world, Vector3 point, float radius, float height, LayerMask blockers, Collider owner)
        {
            Capsule(point, radius, height, out Vector3 bottom, out Vector3 top);
            int count = world.OverlapCapsule(bottom, top, radius, _overlaps, blockers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (_overlaps[i] != owner) { return false; }
            }
            return count < _overlaps.Length;
        }

        private static void Capsule(Vector3 feet, float radius, float height, out Vector3 bottom, out Vector3 top)
        {
            const float floorClearance = 0.06f;
            bottom = feet + Vector3.up * (radius + floorClearance);
            top = feet + Vector3.up * (Mathf.Max(radius, height - radius) + floorClearance);
        }

        private static bool HasSight(PhysicsScene world, Vector3 from, Vector3 to, LayerMask blockers, Collider target)
        {
            Vector3 offset = to - from;
            float distance = offset.magnitude;
            return distance <= 0.001f || !world.Raycast(from, offset / distance, out RaycastHit hit, distance,
                blockers, QueryTriggerInteraction.Ignore) || hit.collider == target;
        }
    }
}
