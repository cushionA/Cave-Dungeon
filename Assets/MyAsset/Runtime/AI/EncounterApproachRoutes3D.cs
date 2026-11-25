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
        private const int k_MaxJumpLinks = 12;
        private const int k_JumpArcSamples = 16;
        private const float k_LandingTolerance = 0.24f;
        private const float k_FailedJumpCooldown = 1f;

        private readonly struct JumpArc
        {
            public readonly Vector3 landing;
            public readonly float duration;
            public readonly float groundSpeed;

            public JumpArc(Vector3 landing, float duration, float groundSpeed)
            {
                this.landing = landing;
                this.duration = duration;
                this.groundSpeed = groundSpeed;
            }
        }

        private sealed class Route
        {
            public readonly Vector3[] steps = new Vector3[k_MaxWaypoints + 1];
            public readonly EncounterJumpLink3D[] jumpSteps = new EncounterJumpLink3D[k_MaxWaypoints + 1];
            public ActorRef target;
            public Vector3 targetPosition;
            public int next = -1;
            public float retryAt;
            public float stalledTime;
            public float bestDistance;
            public EncounterJumpLink3D failedJump;
            public float failedJumpUntil;
            public JumpArc arc;
            public float flightDeadline;
            public bool airborne;
            public bool interrupted;
            public bool discardAfterLanding;

            public void ResetProgress()
            {
                stalledTime = 0f;
                bestDistance = float.PositiveInfinity;
            }
        }

        private readonly Dictionary<ActorRef, Route> _routes = new();
        private readonly List<ActorRef> _retired = new();
        private readonly Vector3[] _nodes = new Vector3[k_MaxWaypoints + 1];
        private readonly Transform[] _markers = new Transform[k_MaxWaypoints + 1];
        private readonly EncounterJumpLink3D[] _jumps = new EncounterJumpLink3D[k_MaxWaypoints + 1];
        private readonly float[] _costs = new float[k_MaxWaypoints + 1];
        private readonly int[] _previous = new int[k_MaxWaypoints + 1];
        private readonly bool[] _settled = new bool[k_MaxWaypoints + 1];
        private readonly Collider[] _overlaps = new Collider[8];

        internal void Remove(ActorRef owner) => _routes.Remove(owner);

        internal void Clear() => _routes.Clear();

        internal bool IsCrossing(ActorRef owner) => _routes.TryGetValue(owner, out Route route) && route.airborne;

        internal bool FinishCurrentJumps()
        {
            bool pending = false;
            foreach (KeyValuePair<ActorRef, Route> item in _routes)
            {
                if (item.Value.airborne && MultiLockTargeting3D.TryGetActor(item.Key, out _))
                {
                    item.Value.discardAfterLanding = true;
                    pending = true;
                }
                else { _retired.Add(item.Key); }
            }
            foreach (ActorRef owner in _retired) { _routes.Remove(owner); }
            _retired.Clear();
            return pending;
        }

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
            Transform[] waypoints, EncounterJumpLink3D[] jumpLinks, LayerMask groundLayers, LayerMask blockers, out bool jumpPressed)
        {
            jumpPressed = false;
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
            if (route.target != sighting.Target) { route.failedJump = default; }
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

            while (route.next >= 0 && route.jumpSteps[route.next].Takeoff == null
                && (route.steps[route.next] - feet).sqrMagnitude <= k_WaypointArrival * k_WaypointArrival)
            {
                route.next--;
                route.ResetProgress();
            }
            if (route.next >= 0 && route.jumpSteps[route.next].Takeoff == null)
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
                FindRoute(route, actor, world, target.Body, feet, sighting, attackRange, waypoints, jumpLinks, groundLayers, blockers);
            }
            if (route.next < 0) { return Vector3.zero; }
            EncounterJumpLink3D jump = route.jumpSteps[route.next];
            if (jump.Takeoff == null) { return Horizontal(route.steps[route.next] - feet).normalized; }

            // グラフ探索から踏切に着くまでに、床や頭上の遮蔽が変わることがある。
            if (jumpLinks == null || !TryGetSupport(world, jump.Takeoff.position, groundLayers, out RaycastHit support)
                || actor.Motor.Ground.Surface != support.collider
                || !TryGetJumpArc(actor, world, feet, jump, groundLayers, blockers, out route.arc))
            {
                RejectJump(route, jump);
                return Vector3.zero;
            }
            route.airborne = true;
            route.interrupted = false;
            route.flightDeadline = Time.fixedTime + route.arc.duration + k_RetryDelay;
            jumpPressed = true;
            return AirDirection(actor.Motor, route.arc);
        }

        internal bool TryContinueJump(CharacterActor3D actor, bool movementBlocked, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (!_routes.TryGetValue(actor.Actor, out Route route) || !route.airborne) { return false; }

            CharacterMotor3D motor = actor.Motor;
            if (motor.IsGrounded)
            {
                if (route.discardAfterLanding)
                {
                    Remove(actor.Actor);
                    return false;
                }
                EncounterJumpLink3D jump = route.jumpSteps[route.next];
                route.airborne = false;
                if (motor.Ground.Surface == jump.LandingSurface
                    && (motor.FeetPosition - route.arc.landing).sqrMagnitude <= k_LandingTolerance * k_LandingTolerance)
                {
                    route.next--;
                    route.ResetProgress();
                }
                else { RejectJump(route, jump); }
                return false;
            }

            route.interrupted |= movementBlocked || Time.fixedTime >= route.flightDeadline
                || (motor.Collisions & (CollisionFlags.Above | CollisionFlags.Sides)) != 0;
            if (!route.interrupted) { direction = AirDirection(motor, route.arc); }
            return true;
        }

        private static Vector3 AirDirection(CharacterMotor3D motor, in JumpArc arc)
        {
            float speed = motor.MoveSpeed;
            return Vector3.ClampMagnitude(Horizontal(arc.landing - motor.FeetPosition) / (speed * Time.fixedDeltaTime),
                arc.groundSpeed / speed);
        }

        private static void RejectJump(Route route, in EncounterJumpLink3D jump)
        {
            route.failedJump = jump;
            route.failedJumpUntil = Time.fixedTime + k_FailedJumpCooldown;
            route.next = -1;
            route.retryAt = 0f;
        }

        private void FindRoute(Route route, CharacterActor3D actor, PhysicsScene world, CharacterController target,
            Vector3 feet, in EncounterSighting3D sighting, float attackRange, Transform[] waypoints,
            EncounterJumpLink3D[] jumpLinks, LayerMask groundLayers, LayerMask blockers)
        {
            Scene scene = actor.gameObject.scene;
            CharacterController body = actor.Body;
            _nodes[0] = feet;
            int count = 1;
            for (int i = 0; i < Mathf.Min(waypoints.Length, k_MaxWaypoints); i++)
            {
                Transform marker = waypoints[i];
                if (marker != null && marker.gameObject.activeInHierarchy && marker.gameObject.scene == scene
                    && TryGetGround(world, marker.position, groundLayers, out Vector3 point))
                {
                    _markers[count] = marker;
                    _nodes[count++] = point;
                }
            }
            for (int i = 0; i < count; i++)
            {
                _costs[i] = float.PositiveInfinity;
                _previous[i] = -1;
                _settled[i] = false;
                _jumps[i] = default;
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
                    if (cost >= _costs[neighbor] || cost >= goalCost) { continue; }
                    EncounterJumpLink3D jump = default;
                    if (!CanWalk(world, start, end, radius, height, groundLayers, blockers))
                    {
                        if (current == 0 || !TryGetJumpLink(route, actor, world, current, neighbor, jumpLinks,
                            groundLayers, blockers, out jump, out float jumpCost)) { continue; }
                        cost = currentCost + jumpCost;
                        if (cost >= _costs[neighbor] || cost >= goalCost) { continue; }
                    }
                    _costs[neighbor] = cost;
                    _previous[neighbor] = current;
                    _jumps[neighbor] = jump;
                }
            }

            if (goalNode < 0) { return; }
            route.steps[++route.next] = goal;
            route.jumpSteps[route.next] = default;
            for (int node = goalNode; node > 0; node = _previous[node])
            {
                route.steps[++route.next] = _nodes[node];
                route.jumpSteps[route.next] = _jumps[node];
            }
        }

        private bool TryGetJumpLink(Route route, CharacterActor3D actor, PhysicsScene world, int from, int to,
            EncounterJumpLink3D[] links, LayerMask groundLayers, LayerMask blockers, out EncounterJumpLink3D jump, out float cost)
        {
            jump = default;
            cost = 0f;
            if (links == null) { return false; }
            for (int i = 0; i < Mathf.Min(links.Length, k_MaxJumpLinks); i++)
            {
                EncounterJumpLink3D link = links[i];
                if (link.Takeoff != _markers[from] || link.Landing != _markers[to]
                    || (Time.fixedTime < route.failedJumpUntil
                        && link.Takeoff == route.failedJump.Takeoff && link.Landing == route.failedJump.Landing)
                    || !TryGetJumpArc(actor, world, _nodes[from], link, groundLayers, blockers, out JumpArc arc)) { continue; }
                jump = link;
                cost = arc.duration * actor.Motor.MoveSpeed;
                return true;
            }
            return false;
        }

        private bool TryGetJumpArc(CharacterActor3D actor, PhysicsScene world, Vector3 start, in EncounterJumpLink3D link,
            LayerMask groundLayers, LayerMask blockers, out JumpArc arc)
        {
            arc = default;
            Scene scene = actor.gameObject.scene;
            if (link.Takeoff == null || link.Landing == null || link.LandingSurface == null
                || !link.Takeoff.gameObject.activeInHierarchy || !link.Landing.gameObject.activeInHierarchy
                || link.Takeoff.gameObject.scene != scene || link.Landing.gameObject.scene != scene || link.LandingSurface.gameObject.scene != scene
                || !TryGetGround(world, link.Takeoff.position, groundLayers, out Vector3 takeoff)
                || (start - takeoff).sqrMagnitude > k_LandingTolerance * k_LandingTolerance
                || !TryGetSupport(world, link.Landing.position, groundLayers, out RaycastHit landing)
                || landing.collider != link.LandingSurface || Mathf.Abs(landing.point.y - takeoff.y) > k_FloorTolerance)
            {
                return false;
            }

            CharacterMotor3D motor = actor.Motor;
            float speed = motor.MoveSpeed;
            float gravity = motor.Gravity;
            float launch = motor.JumpLaunchSpeed;
            Vector3 horizontal = Horizontal(landing.point - start);
            float distance = horizontal.magnitude;
            float discriminant = launch * launch - 2f * gravity * (landing.point.y - start.y);
            if (!(speed > 0f) || !(gravity > 0f) || !(launch > 0f) || !(distance > 0f) || !(discriminant > 0f)) { return false; }
            float duration = (launch + Mathf.Sqrt(discriminant)) / gravity;
            float travelTime = duration - Time.fixedDeltaTime;
            if (!(travelTime > 0f) || duration > 2f || distance > speed * travelTime) { return false; }

            Bounds body = actor.Body.bounds;
            float radius = Mathf.Min(body.extents.x, body.extents.z);
            float height = body.size.y;
            if (!IsClearAt(world, landing.point, radius, height, blockers, actor.Body)) { return false; }
            Vector3 previous = start;
            for (int i = 1; i <= k_JumpArcSamples; i++)
            {
                float time = duration * i / k_JumpArcSamples;
                Vector3 point = start + horizontal * Mathf.Min(time / travelTime, 1f)
                    + Vector3.up * (launch * time - 0.5f * gravity * time * time);
                Vector3 delta = point - previous;
                float length = delta.magnitude;
                Capsule(previous, radius, height, out Vector3 bottom, out Vector3 top);
                if (length > 0.001f && world.CapsuleCast(bottom, top, radius, delta / length, out _, length,
                    blockers, QueryTriggerInteraction.Ignore)) { return false; }
                previous = point;
            }
            arc = new JumpArc(landing.point, duration, distance / travelTime);
            return true;
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
            bool found = TryGetSupport(world, point, groundLayers, out RaycastHit hit);
            ground = found ? hit.point : default;
            return found;
        }

        private static bool TryGetSupport(PhysicsScene world, Vector3 point, LayerMask groundLayers, out RaycastHit hit)
        {
            hit = default;
            return float.IsFinite(point.sqrMagnitude) && world.Raycast(point + Vector3.up * k_FloorTolerance, Vector3.down,
                out hit, 2f * k_FloorTolerance, groundLayers, QueryTriggerInteraction.Ignore) && hit.normal.y >= 0.9f;
        }

        private static Vector3 Horizontal(Vector3 vector) => new(vector.x, 0f, vector.z);

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
