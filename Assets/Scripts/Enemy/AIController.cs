﻿using UnityEngine;
using System.Linq;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;


namespace UnknownWorld.Behaviour
{
    [System.Serializable]
    public class AIController : MonoBehaviour
    {
        [SerializeField] private UnknownWorld.Behaviour.AIAnimationController m_animation;

        [SerializeField] [Range(0, 1)] private float m_targetUpdateDelay = 0.1f;
        [SerializeField] [Range(0, 1)] private float m_pathUpdateDelay = 0.1f;
        [SerializeField] private bool m_isTargetUltimate = true;
        [SerializeField] private bool m_avoidOtherAi = true;
        [SerializeField] private bool m_aiActive = true;
        
        [SerializeField] private List<UnknownWorld.Weapon.WeaponBase> m_weapons;
        [SerializeField] private int m_activeWeapon = 0;

        private UnknownWorld.Path.PathContainer m_targetsSuspicion;
        private UnknownWorld.Path.PathContainer m_targetsDirect;
        private UnknownWorld.Path.PathContainer m_path;
        private bool m_isTargetReselect = false;
        private bool m_isPathAvoidable = false;
        private Coroutine m_updateCorotation;
        private float m_timeSincePathUpdate;
        private NavMeshObstacle m_obstacle;
        private Vector3 m_pointTransform;
        private AIBehaviour m_behaviour;
        private bool m_isActive = false;        
        private NavMeshAgent m_agent;
        private int m_pathIndex;
        private float m_speed;        

        public List<UnknownWorld.Weapon.WeaponBase> Weapons
        {
            get {
                return this.m_weapons ??
                    (this.m_weapons = new List<UnknownWorld.Weapon.WeaponBase>());
            }
            set { this.m_weapons = value; }
        }
        public AIBehaviour Behaviour
        {
            get { return this.m_behaviour; }
        }
        public bool IsTargetUltimate
        {
            get { return this.m_isTargetUltimate; }
            set { this.m_isTargetUltimate = value; }
        }
        public bool IsPathAvoidable
        {
            get { return this.m_isPathAvoidable; }
            set
            {
                if (this.m_isPathAvoidable == value) return;

                this.m_isPathAvoidable = value;
            }
        }
        public bool IsActive
        {
            get { return this.m_isActive; }
            set
            {
                if (this.m_isActive == value) return;

                this.m_isActive = value;

                if (!this.m_isActive)
                {
                    if (this.m_updateCorotation != null)
                        StopCoroutine(this.m_updateCorotation);
                }
                else
                {
                    this.m_updateCorotation = StartCoroutine("UpdateTargets", this.m_targetUpdateDelay);
                }
            }

        }


        private void Start()
        {
            if(m_animation == null)
                m_animation = GetComponent<UnknownWorld.Behaviour.AIAnimationController>();
            m_obstacle = GetComponent<NavMeshObstacle>();
            m_agent = GetComponent<NavMeshAgent>();            

            m_targetsSuspicion = gameObject.AddComponent<UnknownWorld.Path.PriorityClosenessPath>();
            m_targetsDirect = gameObject.AddComponent<UnknownWorld.Path.PriorityClosenessPath>();

            m_path = GetComponent<UnknownWorld.Path.SimplePath>();
            m_behaviour = GetComponent<AIBehaviour>();

            m_agent.updateRotation = false;
            m_agent.autoBraking = false;
            m_agent.autoRepath = false;
            
            if ((m_pathIndex >= 0) && (m_pathIndex < m_path.Length)) {
                UpdatePath(m_path.GetDestination(ref m_pathIndex));
                m_agent.speed = m_path.GetPoint(m_pathIndex).MovementSpeed;
            }
            else // empty path or other einconsistencies
            {
                m_pathIndex = 0;
                m_path.Add(new Path.Data.PathPoint());
                m_path.Points[m_pathIndex].Point = Instantiate(UnknownWorld.Path.Data.PathHelper.PathPrefab, UnknownWorld.Path.Data.PathHelper.PathSpawner.transform).
                                    GetComponent<Path.IntermediatePoint>();

                m_path.Points[m_pathIndex].Transform.position = transform.position;
                m_path.Points[m_pathIndex].Type = Path.Data.PointType.PathFollowing;
                m_path.Points[m_pathIndex].Action = Path.Data.PointAction.Stop;
                m_path.Points[m_pathIndex].TransferDelay = 1.0f;
            }

            // only when 1 weapon or order not required
            Weapons = GetComponentsInChildren<UnknownWorld.Weapon.WeaponBase>().
                OfType<UnknownWorld.Weapon.WeaponBase>().ToList();            

            m_weapons[m_activeWeapon].Activate();

            IsActive = m_aiActive;
            IsPathAvoidable = m_avoidOtherAi;            
        }
        
        private void Update()
        {
            if (!m_isActive) return;

            if (m_behaviour.IsDeath)
            {
                if (!m_animation.IsDead)
                    m_animation.Dead();
                return;
            }

            m_timeSincePathUpdate += Time.deltaTime;

            // set attack distance based on current weapon
            m_behaviour.AttackDistance = m_weapons[m_activeWeapon].Range;

            // when AI use direct or suspicion target that was changed
            if (m_isTargetReselect)
                ReselectTarget();

            // when affection data was processed
            if (m_behaviour.AffectionState == Utility.Data.DataState.Processed)
            {
                switch (m_behaviour.State)
                {
                    case AIBehaviour.AIState.FollowingPath:
                        CheckTargets();
                        break;
                    case AIBehaviour.AIState.ReturningPath:
                        CheckTargets();
                        break;
                    case AIBehaviour.AIState.FollowingSuspicion:
                        CheckIsSuspicion();
                        break;
                    case AIBehaviour.AIState.FollowingTarget:
                        CheckIsTarget();
                        break;
                    case AIBehaviour.AIState.Waiting:
                        CheckTargets();
                        break;
                    case AIBehaviour.AIState.Attacking:
                    case AIBehaviour.AIState.Reloading:
                    case AIBehaviour.AIState.Dead:
                        return;
                }
            }

            if ((m_behaviour.State != AIBehaviour.AIState.Attacking) &&
                (m_behaviour.State != AIBehaviour.AIState.Reloading))
            {
                UpdateVelocity();
                CheckNearness();
            }            
        }
        

        private void CheckTargets()
        {
            if (m_targetsDirect.Length != 0) // if found direct target
            {
                UpdatePath(
                    m_targetsDirect.GetDestination(ref m_pathIndex),
                    true
                );

                m_behaviour.State = AIBehaviour.AIState.FollowingTarget;
            }
            else if (m_targetsSuspicion.Length != 0) // if found suspicion target
            {
                UpdatePath(
                    m_targetsSuspicion.GetDestination(ref m_pathIndex),
                    true
                );

                m_behaviour.State = AIBehaviour.AIState.FollowingSuspicion;
            }
        }

        private void CheckIsTarget()
        {
            if (m_targetsDirect.Length == 0) // if direct target was losted
            {
                SelectClosestTarget();
            }
            else if ((!m_isTargetUltimate) &&
                 (m_targetsDirect.Length > 1)) // if the are more, than 1 direct target and target selection not ultimate
            {
                UpdatePath(
                    m_targetsDirect.GetDestination(ref m_pathIndex),
                    true
                );
            }
            else // update direct target position for NavMeshAgent
            {
                UpdatePath(
                    m_targetsDirect.GetPoint(m_pathIndex).Transform.position
                );
            }            
        }

        private bool IsTargetValid()
        {
            if (m_behaviour.State != AIBehaviour.AIState.FollowingTarget) return false;

            return m_behaviour.Manager.IsTargetValid(m_targetsDirect.Points[m_pathIndex].Point);
        }

        private void CheckIsSuspicion()
        {
            if (m_targetsDirect.Length != 0) // if found direct target
            {
                UpdatePath(
                    m_targetsDirect.GetDestination(ref m_pathIndex),
                    true
                );

                m_behaviour.State = AIBehaviour.AIState.FollowingTarget;
            }
            else if (m_targetsSuspicion.Length == 0) // if was lost suspicion target
            {
                UpdatePath(
                    m_path.GetClosestPoint(ref m_pathIndex),
                    true
                );
                m_behaviour.State = AIBehaviour.AIState.ReturningPath;
            }
            else if ((!m_isTargetUltimate) &&
                     (m_targetsSuspicion.Length > 1)) // if the are more, than 1 suspicion target and target selection not ultimate
            {
                UpdatePath(
                    m_targetsSuspicion.GetDestination(ref m_pathIndex),
                    true
                );
            }
        }


        private void CheckNearness()
        {
            switch (m_behaviour.State)
            {
                case AIBehaviour.AIState.FollowingPath:

                case AIBehaviour.AIState.ReturningPath:
                    if ((!m_agent.pathPending) &&
                        ((m_pathIndex >= 0) && (m_pathIndex < m_path.Length)) &&
                        (m_agent.remainingDistance <= m_path.GetPoint(m_pathIndex).AccuracyRadius))
                    {
                        UpdatePathAction();
                    }
                    break;
                case AIBehaviour.AIState.FollowingSuspicion:
                    if ((!m_agent.pathPending) &&
                        ((m_pathIndex >= 0) && (m_pathIndex < m_targetsSuspicion.Length)) &&
                        (m_agent.remainingDistance <= m_targetsSuspicion.GetPoint(m_pathIndex).AccuracyRadius))
                    {
                        UpdateSuspicionAction();
                    }
                    break;
                case AIBehaviour.AIState.FollowingTarget:
                    if ((!m_agent.pathPending) &&
                        (m_agent.remainingDistance <= m_weapons[m_activeWeapon].Range))
                    {
                        UpdateDirectAction();
                    }
                    break;
            }
        }

        private void UpdateVelocity()
        {
            m_speed = 0.0f;

            if (((m_behaviour.State == AIBehaviour.AIState.FollowingPath) || (m_behaviour.State == AIBehaviour.AIState.ReturningPath)) &&
                ((m_pathIndex >= 0) && (m_pathIndex < m_path.Length)) &&
                (m_agent.remainingDistance > m_path.GetPoint(m_pathIndex).AccuracyRadius))
            {
                m_speed = m_path.GetPoint(m_pathIndex).MovementSpeed;
                m_path.CalculateSpeedOnPath(ref m_speed, transform.position);
            }
            else if ((m_behaviour.State == AIBehaviour.AIState.FollowingTarget) &&
                ((m_pathIndex >= 0) && (m_pathIndex < m_targetsDirect.Length)) &&
                (m_agent.remainingDistance > m_targetsDirect.GetPoint(m_pathIndex).AccuracyRadius))
            {
                m_speed = m_targetsDirect.GetPoint(m_pathIndex).MovementSpeed;
                m_targetsDirect.CalculateSpeedOnPath(ref m_speed, transform.position);
            }
            else if ((m_behaviour.State == AIBehaviour.AIState.FollowingSuspicion) &&
                ((m_pathIndex >= 0) && (m_pathIndex < m_targetsSuspicion.Length)) &&
                (m_agent.remainingDistance > m_targetsSuspicion.GetPoint(m_pathIndex).AccuracyRadius))
            {
                m_speed = m_targetsSuspicion.GetPoint(m_pathIndex).MovementSpeed;
                m_targetsSuspicion.CalculateSpeedOnPath(ref m_speed, transform.position);
            }

            m_agent.speed = m_speed;
            m_animation.Move(m_agent.desiredVelocity, false, false);
        }


        private void UpdatePathAction()
        {
            switch (m_path.GetPoint(m_pathIndex).Action)
            {
                case Path.Data.PointAction.ContinuePath:
                    OnUpdatePathDestinationNext();
                    break;
                case Path.Data.PointAction.Stop:
                    m_agent.speed = 0.0f;
                    if (m_behaviour.State == AIBehaviour.AIState.FollowingPath)
                    {
                        m_behaviour.State = AIBehaviour.AIState.Waiting;
                        Invoke("OnUpdatePathDestinationNext", m_path.GetPoint(m_pathIndex).TransferDelay);
                    }
                    else
                    {
                        m_behaviour.State = AIBehaviour.AIState.Waiting;
                        Invoke("OnUpdatePathDestination", m_path.GetPoint(m_pathIndex).TransferDelay);
                    }
                    break;             
            }
        }
        
        private void UpdateDirectAction()
        {
            switch (m_targetsDirect.GetPoint(m_pathIndex).Action)
            {
                case Path.Data.PointAction.ContinuePath:
                    OnUpdateDirectAction();
                    break;
                case Path.Data.PointAction.Stop:
                    m_agent.speed = 0.0f;
                    m_behaviour.State = AIBehaviour.AIState.Waiting;

                    Invoke("OnUpdateDirectAction", m_targetsDirect.GetPoint(m_pathIndex).TransferDelay); // wait attack time
                    break;
                case Path.Data.PointAction.Attack:
                    if (m_behaviour.State == AIBehaviour.AIState.Attacking)
                        return;

                    m_agent.speed = 0.0f;

                    if ((m_animation.IsActionPerfomerable()) &&
                        IsTargetValid() &&
                        (m_weapons[m_activeWeapon].DoShot()))
                    {
                        m_behaviour.State = AIBehaviour.AIState.Attacking;                        
                        m_animation.Attack(m_weapons[m_activeWeapon].ShotTime);
                        Invoke("OnUpdateDirectAction", m_weapons[m_activeWeapon].ShotTime);
                    }
                    else
                        OnUpdateDirectAction();
                    break;
            }
        }

        private void UpdateSuspicionAction()
        {
            switch (m_targetsSuspicion.GetPoint(m_pathIndex).Action)
            {
                case Path.Data.PointAction.ContinuePath:
                    OnUpdateSuspicionAction();
                    break;
                case Path.Data.PointAction.Stop:
                    m_agent.speed = 0.0f;
                    m_behaviour.State = AIBehaviour.AIState.Waiting;

                    Invoke("OnUpdateSuspicionAction", m_targetsSuspicion.GetPoint(m_pathIndex).TransferDelay);
                    break;
            }
        }


        private void OnUpdateDirectAction()
        {//possible bug(of skipping path point without wait timeout), was solwed with m_agent.pathPending
            if ((m_behaviour.State == AIBehaviour.AIState.Waiting) ||
                (m_behaviour.State == AIBehaviour.AIState.Attacking) ||
                (m_behaviour.State == AIBehaviour.AIState.FollowingTarget))
            {
                SelectClosestTarget();                
                //m_behaviour.State = AIBehaviour.AIState.FollowingTarget;
            }
        }

        private void OnUpdatePathDestination()
        {
            if ((m_behaviour.State == AIBehaviour.AIState.Waiting) ||
                (m_behaviour.State == AIBehaviour.AIState.FollowingPath) ||
                (m_behaviour.State == AIBehaviour.AIState.ReturningPath))
            {
                UpdatePath(
                    m_path.GetClosestPoint(ref m_pathIndex),
                    true
                );
                m_behaviour.State = AIBehaviour.AIState.FollowingPath;
            }
        }

        private void OnUpdateSuspicionAction()
        {
            if ((m_behaviour.State == AIBehaviour.AIState.Waiting) ||
                (m_behaviour.State == AIBehaviour.AIState.FollowingSuspicion))
            {
                UnknownWorld.Path.Data.PathHelper.ClearAll(m_targetsSuspicion.Points);
                m_isTargetReselect = true;

                m_behaviour.State = AIBehaviour.AIState.FollowingSuspicion;
            }            
        }

        private void OnUpdatePathDestinationNext()
        {
            if ((m_behaviour.State == AIBehaviour.AIState.Waiting) ||
                (m_behaviour.State == AIBehaviour.AIState.FollowingPath) ||
                (m_behaviour.State == AIBehaviour.AIState.ReturningPath))
            {
                UpdatePath(
                    m_path.GetDestination(ref m_pathIndex),
                    true
                );
                m_behaviour.State = AIBehaviour.AIState.FollowingPath;
            }
        }


        private void ReselectTarget()
        {
            switch (m_behaviour.State)
            {
                case AIBehaviour.AIState.FollowingTarget:
                case AIBehaviour.AIState.FollowingSuspicion:
                    m_isTargetReselect = false;
                    SelectClosestTarget();                    
                    break;
            }
        }

        private void SelectClosestTarget()
        {
            if (m_targetsDirect.Length != 0) // if found direct target
            {
                UpdatePath(
                    m_targetsDirect.GetDestination(ref m_pathIndex),
                    true
                );

                m_behaviour.State = AIBehaviour.AIState.FollowingTarget;
            }
            else if (m_targetsSuspicion.Length != 0) // if found suspicion target
            {
                UpdatePath(
                    m_targetsSuspicion.GetDestination(ref m_pathIndex),
                    true
                );

                m_behaviour.State = AIBehaviour.AIState.FollowingSuspicion;
            }
            else // returning to path
            {
                UpdatePath(
                    m_path.GetClosestPoint(ref m_pathIndex),
                    true
                );

                m_behaviour.State = AIBehaviour.AIState.ReturningPath;
            }
        }

        private void UpdatePath(Vector3 destination = new Vector3(), bool isImmediate = false)
        {
            if ((!isImmediate) &&
                (m_timeSincePathUpdate < m_pathUpdateDelay))
                return;            

            m_agent.SetDestination((destination == Vector3.zero) ? 
                m_agent.destination : destination);

            m_timeSincePathUpdate = 0.0f;
        }

        private IEnumerator UpdateTargets(float delay)
        {
            while (true)
            {
                yield return new WaitForSeconds(delay);

                if (m_behaviour.AffectionState != Utility.Data.DataState.Transferred)
                    continue;

                // if found new suspicion target
                if (m_behaviour.IsSuspicionTargetDetected)
                {
                    UnknownWorld.Path.Data.PathHelper.ClearAll(m_targetsSuspicion.Points);
                    m_targetsSuspicion.ResetToZero();

                    // reselect target based on state
                    if (m_behaviour.State == AIBehaviour.AIState.FollowingSuspicion)
                        m_isTargetReselect = true;
                }
                else
                {
                    if (m_behaviour.State == AIBehaviour.AIState.FollowingSuspicion)
                    {
                        UnknownWorld.Path.Data.PathHelper.ClearExceptOne(m_targetsSuspicion.Points, ref m_pathIndex);
                    }
                    else if (m_behaviour.State != AIBehaviour.AIState.Waiting)
                    {                        
                        UnknownWorld.Path.Data.PathHelper.ClearAll(m_targetsSuspicion.Points);
                        m_targetsSuspicion.ResetToZero();

                        m_isTargetReselect = true;
                    }
                }

                // if found new direct target
                if (m_behaviour.IsDirectTargetDetected)
                {
                    for (int i = 0; i < m_targetsDirect.Length; i++)
                    {
                        if (UnknownWorld.Path.Data.PathHelper.
                            IsTargetIn(m_targetsDirect.Points[i].Transform, m_targetsDirect.Points[i].Type, m_behaviour.PossibleTargets))
                            continue;

                        // copy lost direct target to remake it into suspicion target
                        Path.Data.PathPoint tmp = new Path.Data.PathPoint(m_targetsDirect.GetPoint(i));

                        Vector3 position = tmp.Transform.position;
                        tmp.Point = Instantiate(UnknownWorld.Path.Data.PathHelper.PathPrefab, UnknownWorld.Path.Data.PathHelper.PathSpawner.transform).GetComponent<Path.IntermediatePoint>();
                        tmp.Transform.position = position;

                        tmp.Type = Path.Data.PointType.FollowingSuspicion;
                        tmp.Action = Path.Data.PointAction.Stop;

                        tmp.AccuracyRadius = m_behaviour.Collider.radius;                        
                        tmp.TransferDelay = 1.0f;

                        // adding new suspicion target and clearing direct targets
                        m_targetsSuspicion.Add(tmp);

                        // reselect target based on state
                        if ((i == m_pathIndex) &&
                            (!m_isTargetReselect) &&
                            (m_behaviour.State == AIBehaviour.AIState.FollowingTarget))
                            m_isTargetReselect = true;

                        // removing direct target, that was lost
                        m_targetsDirect.RemoveAt(i--);
                    }                   
                }
                else
                {
                    if (m_behaviour.State == AIBehaviour.AIState.FollowingTarget)
                    {
                        UnknownWorld.Path.Data.PathHelper.ClearExceptOne(m_targetsDirect.Points, ref m_pathIndex);

                        // copy lost direct target to remake it into suspicion target
                        Path.Data.PathPoint tmp = new Path.Data.PathPoint(m_targetsDirect.GetPoint(m_pathIndex));

                        Vector3 position = tmp.Transform.position;
                        tmp.Point = Instantiate(UnknownWorld.Path.Data.PathHelper.PathPrefab, UnknownWorld.Path.Data.PathHelper.PathSpawner.transform).GetComponent<Path.IntermediatePoint>();
                        tmp.Transform.position = position;

                        tmp.Type = Path.Data.PointType.FollowingSuspicion;
                        tmp.Action = Path.Data.PointAction.Stop;

                        tmp.AccuracyRadius = m_behaviour.Collider.radius /** 10*/;
                        tmp.TransferDelay = 1.0f;
                        
                        // adding new suspicion target and clearing direct targets
                        m_targetsSuspicion.Add(tmp, ref m_pathIndex);

                        m_behaviour.State = AIBehaviour.AIState.FollowingSuspicion;

                        UnknownWorld.Path.Data.PathHelper.ClearAll(m_targetsDirect.Points);
                    }
                    else
                    {
                        for (int i = 0; i < m_targetsDirect.Length; i++)
                        {
                            // copy lost direct target to remake it into suspicion target
                            Path.Data.PathPoint tmp = new Path.Data.PathPoint(m_targetsDirect.GetPoint(i));

                            Vector3 position = tmp.Transform.position;
                            tmp.Point = Instantiate(UnknownWorld.Path.Data.PathHelper.PathPrefab, UnknownWorld.Path.Data.PathHelper.PathSpawner.transform).GetComponent<Path.IntermediatePoint>();
                            tmp.Transform.position = position;

                            tmp.Type = Path.Data.PointType.FollowingSuspicion;
                            tmp.Action = Path.Data.PointAction.Stop;

                            tmp.AccuracyRadius = m_behaviour.Collider.radius;
                            tmp.TransferDelay = 1.0f;

                            // adding new suspicion target and clearing direct targets                            
                            m_targetsSuspicion.Add(tmp);
                        }
                        UnknownWorld.Path.Data.PathHelper.ClearAll(m_targetsDirect.Points);
                    }
                }                
                
                // update targets

                for (int i = 0; i < m_behaviour.PossibleTargets.Count; i++) // add check, if there no same point & type in array
                {
                    if (m_behaviour.PossibleTargets[i].Type == Path.Data.PointType.FollowingTarget)
                    {
                        if (!UnknownWorld.Path.Data.PathHelper.IsTargetIn(m_behaviour.PossibleTargets[i].Transform, m_behaviour.PossibleTargets[i].Type, m_targetsDirect.Points))
                            m_targetsDirect.Add(m_behaviour.PossibleTargets[i]);
                    }
                    else if (m_behaviour.PossibleTargets[i].Type == Path.Data.PointType.FollowingSuspicion)
                    {
                        if (!UnknownWorld.Path.Data.PathHelper.IsTargetIn(m_behaviour.PossibleTargets[i].Transform, m_behaviour.PossibleTargets[i].Type, m_targetsSuspicion.Points))
                            m_targetsSuspicion.Add(m_behaviour.PossibleTargets[i]);
                    }
                }

                m_behaviour.AffectionState = Utility.Data.DataState.Processed;
            }
        }
      
    }
}
