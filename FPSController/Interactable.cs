using System;
using System.Linq;
using Modding;
using Modding.Blocks;
using UnityEngine;

namespace FPSController
{
    public class Interactable : BlockScript
    {
        public MText label;
        public string hint;
        public Controller User { get; protected set; }

        public override void SafeAwake()
        {
            label = AddText("Label", "int-label", "Use");
        }

        public override void OnSimulateStart()
        {
            hint = DoubleSpace(label.Value);
        }

        public virtual void StartInteraction(Controller controller)
        {

        }

        public virtual void StopInteraction()
        {

        }

        private static string DoubleSpace(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return string.Empty;
            }

            char[] a = s.ToCharArray();
            char[] b = new char[(a.Length * 2) - 1];

            int bIndex = 0;
            for (int i = 0; i < a.Length; i++)
            {
                b[bIndex++] = a[i];

                if (i < (a.Length - 1))
                {
                    b[bIndex++] = ' ';
                }
            }

            return new string(b);
        }
    }
}
