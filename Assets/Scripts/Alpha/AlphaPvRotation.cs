using System.Collections.Generic;
using MajdataPlay.Scenes.Game;
using UnityEngine;

namespace MajdataPlay.Alpha
{
    // Counter-rotate the PV source before canvas batching: the following Play
    // postprocess rotates the chart texture, leaving an excluded PV stationary.
    [DefaultExecutionOrder(900)]
    internal sealed class AlphaPvRotation : MonoBehaviour
    {
        readonly Dictionary<Transform, Quaternion> poses = new();
        internal void Register(Transform target)
        {
            if (!target || poses.ContainsKey(target)) return;
            foreach (var pose in poses)
                if (pose.Key && target.IsChildOf(pose.Key)) return;
            var descendants=new List<Transform>();
            foreach(var pose in poses)
                if(pose.Key && pose.Key.IsChildOf(target)) descendants.Add(pose.Key);
            foreach(var child in descendants) { child.localRotation=poses[child]; poses.Remove(child); }
            poses.Add(target,target.localRotation);
        }
        void LateUpdate()
        {
            var clock=Majdata<INoteController>.Instance;
            var timeline=AlphaSimaiParser.Current;
            if (clock == null || timeline == null || !clock.IsStart) { Apply(0); return; }
            var time=clock.ThisFrameSec;
            Apply(timeline.RotationForPV(time)-timeline.ScreenEffect("Rotate",time,out _));
        }
        internal void Apply(float counterDegrees)
        {
            var rotation=Quaternion.Euler(0,0,counterDegrees);
            foreach (var pose in poses)
                if (pose.Key) pose.Key.localRotation=pose.Value*rotation;
        }
        void OnDisable() => Apply(0);
    }
}
