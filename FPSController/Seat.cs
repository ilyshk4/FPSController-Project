using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FPSController
{
    public class Seat : Interactable
    {
        public Controller passenger;

        public override void StartInteraction(Controller controller)
        {
            if (passenger == null)
            {
                passenger = controller;
                passenger.SetSitting(this);
            }
        }
    }
}
