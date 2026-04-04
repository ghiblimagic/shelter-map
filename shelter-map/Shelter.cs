using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace shelter_map
{

    public class ShelterOutcome
    {
        public int DiedInCare { get; set; }
        public int Euthanasia { get; set; }
        public int Missing { get; set; }
    }

    public class NonLiveOutcomes
    {
        public ShelterOutcome Cats { get; set; } = new();
        public ShelterOutcome Kittens { get; set; } = new();
        public ShelterOutcome Dogs { get; set; } = new();
        public ShelterOutcome Totals { get; set; } = new();
    }

    public class OutcomeBreakdown
    {
        public int Adoptions { get; set; }
        public int BestFriends { get; set; }
        public int NewHope { get; set; }
        public int Redeemed { get; set; }
        public int Released { get; set; }
    }

    public class LiveOutcomes
    {
        public OutcomeBreakdown Dogs { get; set; } = new();
        public OutcomeBreakdown Cats { get; set; } = new();
        public OutcomeBreakdown Kittens { get; set; } = new();
        public OutcomeBreakdown Totals { get; set; } = new();
    }
    public class Shelter
    {
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public int Cats { get; set; }
        public int Kittens { get; set; }
        public int Dogs { get; set; }
        public int CatsSaved { get; set; }
        public int KittensSaved { get; set; }
        public int DogsSaved { get; set; }
        public int Total { get; set; }
        public double SaveRate { get; set; }

        public double NonLiveRate { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public LiveOutcomes LiveOutcomes { get; set; } = new();
        public NonLiveOutcomes NonLiveOutcomes { get; set; } = new();
    }
}
