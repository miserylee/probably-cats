using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace DeepSpaceCattery
{
    public enum AgeStage
    {
        Kitten,
        Adult,
        Senior,
        Dead
    }

    public enum CatDeathCause
    {
        None,
        Natural,
        Blade,
        Starvation
    }

    public sealed class CatBirthPlacement
    {
        public string? HouseId { get; set; }
        public bool InNursery { get; set; }
    }

    public sealed class Cat
    {
        private const int CatFoodBenefitNights = 2;
        private const double KittenGrowthBonus = .25;
        private const double AdultAgingReduction = .20;
        private const double AgePrecision = 1e-9;

        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? OriginalName { get; set; }
        public Genome Genome { get; set; } = new Genome();
        public int GenomeRules { get; set; } = 1;
        public string? MotherId { get; set; }
        public string? FatherId { get; set; }
        public int BirthDay { get; set; }
        public int AcquiredDay { get; set; } = int.MinValue;
        public bool Neutered { get; set; }
        public int? NeuteredDay { get; set; }
        public bool Alive { get; set; } = true;
        public bool WasSold { get; set; }
        public bool WasAbandoned { get; set; }
        [JsonIgnore]
        public bool IsInCare => !WasSold && !WasAbandoned;
        public CatDeathCause DeathCause { get; set; }
        public int? DeathDay { get; set; }
        public bool CorpsePlaced { get; set; }
        public bool CorpseCleaned { get; set; }
        public bool CleanupFoodDelivered { get; set; }
        public int Satiety { get; set; } = 100;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int SatietyMissingKcal { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? CatFoodFirstNight { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? CatFoodLastNight { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public double CatFoodGrowthDaysGained { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public double CatFoodAdultDaysSaved { get; set; }
        public bool HasPlacedItem { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public long ShedFurPending { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool RoamingFromMainCompartment { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? RoamingHouseId { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public CatBirthPlacement? PendingBirthPlacement { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? NextBreedingDay { get; set; }
        public int RecoveryDaysRemaining(int day) => Math.Max(0, NextBreedingDay.GetValueOrDefault(day) - day);
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        internal int FailedConceptionNights { get; set; }
        public CatGeneScan? GeneScan { get; set; }
        public bool IsMale => Genome.IsMale;

        internal int StoredCalories => Math.Max(0, Math.Clamp(Satiety, 0, 100) * Cattery.KcalPerSatietyPoint -
            Math.Clamp(SatietyMissingKcal, 0, Cattery.KcalPerSatietyPoint - 1));

        internal void SetStoredCalories(int kcal)
        {
            int available = Math.Clamp(kcal, 0, Cattery.MaximumSatietyKcal);
            Satiety = (available + Cattery.KcalPerSatietyPoint - 1) / Cattery.KcalPerSatietyPoint;
            SatietyMissingKcal = Satiety * Cattery.KcalPerSatietyPoint - available;
        }

        internal void RecordCatFoodMeal(int firstNight)
        {
            int lastNight = (int)Math.Min(int.MaxValue, (long)firstNight + CatFoodBenefitNights - 1);
            if (CatFoodFirstNight is int start && CatFoodLastNight is int end &&
                (long)firstNight <= (long)end + 1 && (long)lastNight >= (long)start - 1)
            {
                CatFoodFirstNight = Math.Min(start, firstNight);
                CatFoodLastNight = Math.Max(end, lastNight);
                return;
            }
            if (CatFoodFirstNight == null || firstNight > CatFoodFirstNight.Value)
            {
                CatFoodFirstNight = firstNight;
                CatFoodLastNight = lastNight;
            }
        }

        internal bool HasCatFoodBenefits(int night) => CatFoodFirstNight is int first &&
            CatFoodLastNight is int last && night >= first && night <= last;

        internal double EffectiveAgeDays(int day) =>
            (double)day - BirthDay + CatFoodGrowthDaysGained - CatFoodAdultDaysSaved;

        internal void AdvanceCatFoodAging(int previousDay, int day, Phenotype traits)
        {
            if (CatFoodFirstNight is not int first || CatFoodLastNight is not int last)
            {
                return;
            }

            long start = Math.Max((long)previousDay + 1, first);
            int end = Math.Min(day, last);
            for (long night = start; night <= end; night++)
            {
                double age = EffectiveAgeDays((int)(night - 1));
                if (age >= traits.JuvenileDays + traits.AdultDays)
                {
                    break;
                }
                var (growth, delay) = CatFoodProgress(age, traits);
                CatFoodGrowthDaysGained += growth;
                CatFoodAdultDaysSaved += delay;
            }
        }

        private static (double Growth, double Delay) CatFoodProgress(double age, Phenotype traits)
        {
            // If a stage ends partway through a night, spend the rest at the next stage's rate.
            double kittenFraction = Math.Clamp((traits.JuvenileDays - age) / (1 + KittenGrowthBonus), 0, 1);
            double ageAfterKitten = age + kittenFraction * (1 + KittenGrowthBonus);
            double adultFraction = Math.Clamp(
                (traits.JuvenileDays + traits.AdultDays - ageAfterKitten) / (1 - AdultAgingReduction),
                0, 1 - kittenFraction);
            return (kittenFraction * KittenGrowthBonus, adultFraction * AdultAgingReduction);
        }

        internal double NaturalDeathDay(Phenotype traits) => BirthDay + Math.Ceiling(
            traits.JuvenileDays + traits.AdultDays + traits.SeniorDays
            - CatFoodGrowthDaysGained + CatFoodAdultDaysSaved - AgePrecision);

        internal int DaysUntilNextStage(int day, Phenotype traits, AgeStage stage)
        {
            double threshold = stage switch
            {
                AgeStage.Kitten => traits.JuvenileDays,
                AgeStage.Adult => traits.JuvenileDays + traits.AdultDays,
                _ => traits.JuvenileDays + traits.AdultDays + traits.SeniorDays
            };
            double age = EffectiveAgeDays(day);
            int days = 0;
            while ((long)day + days < int.MaxValue && HasCatFoodBenefits(day + days + 1))
            {
                var (growth, delay) = CatFoodProgress(age, traits);
                age += 1 + growth - delay;
                days++;
                if (age + AgePrecision >= threshold)
                {
                    return days;
                }
            }
            return days + Math.Max(1, (int)Math.Ceiling(threshold - age - AgePrecision));
        }

        public Phenotype GetPhenotype()
        {
            Phenotype traits = Genetics.Evaluate(Genome);
            if (Neutered)
            {
                traits.Irritability = Math.Max(0, traits.Irritability - 10);
                if (NeuteredDay is int operationDay)
                {
                    double juvenileElapsed = Math.Max(0, (double)operationDay - BirthDay + CatFoodGrowthDaysGained);
                    double juvenileRemaining = Math.Max(0, traits.JuvenileDays - juvenileElapsed);
                    traits.JuvenileDays += .25 * juvenileRemaining;
                }
            }
            return traits;
        }

        public void RecordDeath(CatDeathCause cause, int day)
        {
            Alive = false;
            if (DeathCause == CatDeathCause.None)
            {
                DeathCause = cause;
                DeathDay = day;
            }
        }

        public AgeStage GetStage(int day)
        {
            if (!Alive)
            {
                return AgeStage.Dead;
            }

            double age = EffectiveAgeDays(day) + AgePrecision;
            Phenotype traits = GetPhenotype();
            double adultEnd = traits.JuvenileDays + traits.AdultDays;
            if (age < traits.JuvenileDays)
            {
                return AgeStage.Kitten;
            }
            if (age < adultEnd)
            {
                return AgeStage.Adult;
            }
            return age < adultEnd + traits.SeniorDays ? AgeStage.Senior : AgeStage.Dead;
        }
    }

    public sealed class BreedingJob
    {
        public string MotherId { get; set; } = string.Empty;
        public string FatherId { get; set; } = string.Empty;
        public string ChildId { get; set; } = string.Empty;
        public int StartedDay { get; set; }
        public int DueDay { get; set; }
        public int Seed { get; set; }
        // Jobs saved before litters were introduced still have one kitten.
        public int LitterSize { get; set; } = 1;
    }

    public sealed partial class Cattery
    {
        public const int BreedingDays = 2;
        public const int StarterFood = 4;
        // Original 0.46D AnimalSpecies.Rat hunger-rate base and maximum hunger are 65 and 500 kcal.
        internal const int DailyFoodKcal = 65 * 3;
        internal const int MaximumSatietyKcal = 500 * 3;
        internal const int KcalPerSatietyPoint = MaximumSatietyKcal / 100;
        internal const int CatFoodKcal = 240;
        public const int InitialHouseWidth = 8;
        public const int InitialHouseHeight = 4;
        private const int LitterSeedMask = 0x5A17C9E3;

        public int GenomeRules { get; set; } = 1;
        public int LastDay { get; set; } = -1;
        public int LastPetShopSupplyWeek { get; set; }
        public bool StarterClaimed { get; set; }
        public bool StarterFeederPending { get; set; }
        public bool HasPlacedHouse { get; set; }
        // v0.1.7 stored an upgrade count as a nominal number of cats.
        public int HouseCapacity { get; set; } = 3;
        public int HouseGridWidth { get; set; }
        public int HouseGridHeight { get; set; }
        public int NurseryHeight { get; set; }
        public bool HouseResidentsInitialized { get; set; }
        public int Food { get; set; }
        public List<Cat> Cats { get; set; } = new List<Cat>();
        public List<BreedingJob> BreedingJobs { get; set; } = new List<BreedingJob>();

        public (int Width, int Height) GetHouseSize()
        {
            int legacyCapacity = Math.Max(3, HouseCapacity);
            int width = HouseGridWidth > 0 ? HouseGridWidth : 2 * Math.Min(6, legacyCapacity);
            int height = HouseGridHeight > 0 ? HouseGridHeight : 4 * ((legacyCapacity - 1) / 6 + 1);
            return (Math.Max(InitialHouseWidth, width), Math.Max(InitialHouseHeight, height));
        }

        public bool StartPetShop(int day, Random random)
        {
            if (StarterClaimed)
            {
                return false;
            }

            AddFounder(day, random, false);
            AddFounder(day, random, true);
            StarterClaimed = true;
            StarterFeederPending = true;
            Food += StarterFood;
            return true;
        }

        /// <summary>A new unrelated cat, either newly born or at the first day of breeding age.</summary>
        public Cat AddFounder(int day, Random random, bool male, bool kitten = false)
        {
            if (day < LastDay)
            {
                throw new ArgumentOutOfRangeException(nameof(day), "Cannot add a cat before the saved day.");
            }
            if (LastDay == -1)
            {
                LastDay = day;
            }

            Genome genome = Genetics.Founder(random, male);
            int age = kitten ? 0 : (int)Math.Ceiling(Genetics.Evaluate(genome).JuvenileDays);
            return Register(Guid.NewGuid().ToString("N"), day - age, day, genome, null, null);
        }

        public bool Feed(string catId)
        {
            Cat? cat = Find(catId);
            if (cat == null || !cat.Alive || !cat.IsInCare || Food <= 0 || cat.StoredCalories >= MaximumSatietyKcal)
            {
                return false;
            }

            Food--;
            cat.SetStoredCalories(cat.StoredCalories + CatFoodKcal);
            cat.RecordCatFoodMeal(LastDay + 1);
            return true;
        }

        public BreedingJob? GetPregnancy(string motherId) => BreedingJobs.Find(job => job.MotherId == motherId);

        public bool IsPregnant(string catId) => GetPregnancy(catId) != null;

        public bool IsBreeding(string catId) => IsPregnant(catId);

        public bool CanBreed(Cat cat, int day) => day >= LastDay && Cats.Contains(cat)
            && cat.Alive && cat.IsInCare && !cat.Neutered && cat.StoredCalories > 0
            && cat.GetStage(day) == AgeStage.Adult && cat.RecoveryDaysRemaining(day) == 0 && !IsPregnant(cat.Id);

        public bool TryStartPregnancy(Cat mother, Cat father, int day, int seed) =>
            TryStartPregnancy(mother, father, day, seed, out _);

        public bool TryBreed(string motherId, string fatherId, int day, int seed, out string reason) =>
            TryStartPregnancy(Find(motherId), Find(fatherId), day, seed, out reason);

        private bool TryStartPregnancy(Cat? mother, Cat? father, int day, int seed, out string reason)
        {
            if (mother == null || father == null || mother.IsMale || !father.IsMale
                || !Cats.Contains(mother) || !Cats.Contains(father) || !mother.IsInCare || !father.IsInCare)
            {
                reason = CatText.T("请选择一只母猫和一只公猫。");
                return false;
            }
            if (day < LastDay || mother.GetStage(day) != AgeStage.Adult || father.GetStage(day) != AgeStage.Adult)
            {
                reason = CatText.T("育种需要两只存活的成年猫。");
                return false;
            }
            if (mother.Neutered || father.Neutered)
            {
                reason = CatText.T("绝育后的猫无法育种。");
                return false;
            }
            if (mother.StoredCalories <= 0 || father.StoredCalories <= 0)
            {
                reason = CatText.T("猫需要先吃些东西才能配对。");
                return false;
            }
            if (IsPregnant(mother.Id))
            {
                reason = CatText.T("这只母猫已经怀孕。");
                return false;
            }
            if (mother.RecoveryDaysRemaining(day) > 0)
            {
                reason = CatText.T("母猫仍在产后恢复期。");
                return false;
            }

            BreedingJobs.Add(new BreedingJob
            {
                MotherId = mother.Id,
                FatherId = father.Id,
                ChildId = Guid.NewGuid().ToString("N"),
                StartedDay = day,
                DueDay = checked(day + BreedingDays),
                Seed = seed,
                // Sample once without using any of the later inheritance draws.
                LitterSize = Genetics.SampleLitterSize(new Random(seed ^ LitterSeedMask), mother.Genome)
            });
            mother.FailedConceptionNights = 0;
            reason = string.Empty;
            return true;
        }

        public IReadOnlyList<Cat> AdvanceTo(int day)
        {
            var newborns = new List<Cat>();
            if (day <= LastDay)
            {
                return newborns;
            }
            if (LastDay == -1)
            {
                LastDay = day;
                return newborns;
            }

            // Process due jobs in game-day order, even when reopening a save
            // after several days. Newborns only age after their own birth day.
            BreedingJob[] dueJobs = BreedingJobs.Where(job => job.DueDay <= day)
                .OrderBy(job => job.DueDay).ToArray();
            foreach (BreedingJob job in dueJobs)
            {
                AdvanceCats(Math.Max(LastDay, job.DueDay));
                Cat? mother = Find(job.MotherId);
                Cat? father = Find(job.FatherId);
                if (mother != null && mother.Alive && mother.IsInCare && father != null)
                {
                    mother.NextBreedingDay = checked(job.DueDay + 1 + mother.GetPhenotype().PostpartumRecoveryDays);
                    var random = new Random(job.Seed);
                    int size = Math.Clamp(job.LitterSize, Genetics.MinimumLitterSize, Genetics.MaximumLitterSize);
                    for (int i = 0; i < size; i++)
                    {
                        // Keep the former singleton ID and its first seeded draw.
                        string childId = i == 0 ? job.ChildId : $"{job.ChildId}-{i + 1}";
                        Genome inherited = Genetics.Inherit(random, mother.Genome, father.Genome);
                        if (Find(childId) == null)
                        {
                            newborns.Add(Register(childId, job.DueDay, job.DueDay, inherited, mother.Id, father.Id));
                        }
                    }
                }
                BreedingJobs.Remove(job);
            }

            AdvanceCats(day);
            return newborns;
        }

        private void AdvanceCats(int day)
        {
            if (day <= LastDay)
            {
                return;
            }

            foreach (Cat cat in Cats.Where(cat => cat.Alive))
            {
                if (cat.PendingBirthPlacement != null)
                {
                    // A litter waiting for a visible slot starts aging once it can be cared for.
                    int waitingDays = day - LastDay;
                    cat.BirthDay += waitingDays;
                    if (cat.AcquiredDay != int.MinValue)
                    {
                        cat.AcquiredDay += waitingDays;
                    }
                    continue;
                }
                Phenotype traits = cat.GetPhenotype();
                int firstDay = Math.Max(cat.BirthDay, cat.AcquiredDay);
                int previousDay = Math.Max(LastDay, firstDay);
                if (cat.IsInCare)
                {
                    cat.AdvanceCatFoodAging(previousDay, day, traits);
                }
                double deathDay = cat.NaturalDeathDay(traits);
                double elapsed = Math.Max(0, Math.Min(day, deathDay) - previousDay);
                if (elapsed > 0 && cat.IsInCare)
                {
                    int dailyConsumption = (int)Math.Ceiling(DailyFoodKcal * traits.SatietyDrainFactor);
                    int consumed = (int)Math.Min(MaximumSatietyKcal, elapsed * dailyConsumption);
                    cat.SetStoredCalories(cat.StoredCalories - consumed);
                    int dailyFur = traits.FurLength switch
                    {
                        "hairless" => 0,
                        "long" => 2,
                        _ => 1
                    };
                    cat.ShedFurPending += (long)elapsed * dailyFur;
                }
                if (day >= deathDay)
                {
                    cat.RecordDeath(CatDeathCause.Natural, checked((int)deathDay));
                }
            }
            LastDay = day;
        }

        private Cat Register(string id, int birthDay, int acquiredDay, Genome genome, string? motherId, string? fatherId)
        {
            var cat = new Cat
            {
                Id = id,
                Name = KittenNames.Pick(id, Cats),
                BirthDay = birthDay,
                AcquiredDay = acquiredDay,
                Genome = genome,
                GenomeRules = GenomeRules,
                MotherId = motherId,
                FatherId = fatherId
            };
            Cats.Add(cat);
            return cat;
        }

        private Cat? Find(string catId)
        {
            return Cats.Find(cat => cat.Id == catId);
        }
    }
}
