using System;

namespace DeepSpaceCattery
{
    public sealed class Genome
    {
        // Six phased copies, A through F, stored in physical position order.
        public string[] Maternal { get; set; } = Array.Empty<string>();
        public string[] Paternal { get; set; } = Array.Empty<string>();
        public string MaternalX { get; set; } = "";
        public string PaternalSex { get; set; } = "";
        // A–F then the sex chromosome. Bit i marks a flip at sequence index i in this birth.
        public int[] MaternalMutationMasks { get; set; } = Array.Empty<int>();
        public int[] PaternalMutationMasks { get; set; } = Array.Empty<int>();
        public double PatternSeed { get; set; }
        public double NoseSeed { get; set; }
        public bool IsMale => PaternalSex?.Length == 2;
    }

    public sealed class Phenotype
    {
        public double Fullness { get; internal set; }
        public double Cheeks { get; internal set; }
        public double Muzzle { get; internal set; }
        public double Ears { get; internal set; }
        public double Eyes { get; internal set; }
        public string EarShape { get; internal set; } = "upright";
        public string FurLength { get; internal set; } = "short";
        public string Fur => FurLength;
        public string SkinColor { get; internal set; } = "rose";
        public string BaseColor { get; internal set; } = "ginger";
        public string PatchColor { get; internal set; } = "charcoal";
        public int CoatPattern { get; internal set; }
        public string Marking { get; internal set; } = "none";
        public int MarkingIndex { get; internal set; }
        public double Bands { get; internal set; }
        public double White { get; internal set; }
        public string EyeLeft { get; internal set; } = "amber";
        public string EyeRight { get; internal set; } = "amber";
        public string EyeMode => EyeLeft == EyeRight ? "matched" : "odd";
        public string BrowType { get; internal set; } = "none";
        public string NosePigment { get; internal set; } = "auto";
        public string EarPigment { get; internal set; } = "auto";
        public double PatternSeed { get; internal set; }
        public double NoseSeed { get; internal set; }
        public bool SuppressedWhite { get; internal set; }
        public bool WhiteCoat => FurLength != "hairless" && BaseColor == "chalk" && CoatPattern == 1 && MarkingIndex == 0;
        public bool PaternalNoseSpot { get; internal set; }
        public bool SlowRecovery { get; internal set; }
        public bool BadWaterSensitive { get; internal set; }
        public int BufferCount { get; internal set; }

        public int Affection { get; internal set; }
        public int Irritability { get; internal set; }
        public int Curiosity { get; internal set; }
        public int Activity { get; internal set; }
        public int Nocturnality { get; internal set; }
        public int HuntingDrive { get; internal set; }
        public int Reaction { get; internal set; }
        public int NightHuntingReaction { get; internal set; }
        public int Vigilance { get; internal set; }

        public int SuppressWhiteIrritabilityBonus { get; internal set; }
        public double NoiseAvoidanceBonus { get; internal set; }
        public int UnfamiliarNightSoundAvoidanceBonus { get; internal set; }
        public double ClimbingAndWheelEnergyFactor { get; internal set; }
        public int ScrapHeapSearchBonus { get; internal set; }
        public double SatietyDrainFactor { get; internal set; }
        public double MaximumEnergyFactor { get; internal set; }
        public double RestRecoveryFactor { get; internal set; }
        public double BadWaterIllnessFactor { get; internal set; }
        public double JuvenileDays { get; internal set; }
        public double AdultDays { get; internal set; }
        public double SeniorDays { get; internal set; }
        public int LitterPotential { get; internal set; }
        public int PostpartumRecoveryDays { get; internal set; }
    }

    public static class Genetics
    {
        public const int MinimumLitterSize = 1;
        public const int MaximumLitterSize = 8;

        private static readonly int[] LitterWeightsByDistance = { 60, 27, 11, 5, 2, 1, 1, 1 };
        private static readonly int[] Lengths = { 14, 17, 18, 14, 14, 14 };
        private static readonly int[] Neutral = { 5, 6, 6, 5, 5, 5 };
        private static readonly int[] Reserve = { 10, 0, 0, 0, 0, 0 };
        private static readonly string[] SkinColors = { "rose", "sand", "pearl", "slate", "cocoa" };
        private static readonly string[][] DilutedColors = {
            new[] { "ginger", "cream", "cream" },
            new[] { "black", "charcoal", "blue" },
            new[] { "chocolate", "cinnamon", "fawn" },
            new[] { "blue", "lilac", "silver" },
            new[] { "chalk", "chalk", "chalk" }
        };
        private static readonly string[] PatchColors = {
            "charcoal", "black", "chocolate", "cinnamon", "blue", "silver",
            "lilac", "fawn", "ginger", "cream", "chalk"
        };
        private static readonly string[] EyeColors = { "amber", "gold", "copper", "hazel", "green", "aqua", "blue" };
        private static readonly string[] Brows = { "none", "fine", "bold", "arched", "points" };
        private static readonly string[] Noses = { "auto", "rose", "spotted", "dark" };
        private static readonly string[] InnerEars = { "auto", "rose", "smoke", "dark" };
        private static readonly string[] Markings = { "none", "gloves", "tuxedo", "bib", "point", "van", "harlequin", "spotted", "snowpaws", "inktail", "inkseal" };
        private static readonly int[] MarkingMinWhite = { 0, 1, 2, 1, 0, 2, 2 };
        private static readonly double[] WhiteAmounts = { 0, 0.45, 0.85 };
        private static readonly double[] StripeFactors = { 1.0, 1.15, 1.30 };
        private static readonly int[] WhiteIrritabilityBonus = { 30, 15, 0 };
        private static readonly double[] NoiseFactors = { 1.0, 0.75, 0.50 };
        private static readonly double[] ActivityEnergyFactors = { 1.0, 0.90, 0.80 };

        public static Genome Founder(Random rng, bool male)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            return new Genome
            {
                Maternal = FounderAutosomes(rng),
                Paternal = FounderAutosomes(rng),
                MaternalX = FounderStrand(rng, 'X', 6),
                PaternalSex = male ? FounderStrand(rng, 'Y', 2) : FounderStrand(rng, 'X', 6),
                PatternSeed = rng.NextDouble(),
                NoseSeed = rng.NextDouble()
            };
        }

        public static Genome Inherit(Random rng, Genome mother, Genome father,
            double crossoverChance = .35, double mutationChance = .002)
        {
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            Validate(mother, nameof(mother));
            Validate(father, nameof(father));
            if (mother.IsMale) throw new ArgumentException("The mother must provide two X copies.", nameof(mother));
            if (!father.IsMale) throw new ArgumentException("The father must provide X and Y.", nameof(father));
            ValidateProbability(crossoverChance, nameof(crossoverChance));
            ValidateProbability(mutationChance, nameof(mutationChance));

            string paternalSex = rng.Next(2) == 0 ? father.MaternalX : father.PaternalSex;
            string maternalX = Transmit(rng, mother.MaternalX, mother.PaternalSex, crossoverChance);
            var maternalMasks = new int[Lengths.Length + 1];
            var paternalMasks = new int[Lengths.Length + 1];
            return new Genome
            {
                Maternal = InheritAutosomes(rng, mother, crossoverChance, mutationChance, maternalMasks),
                Paternal = InheritAutosomes(rng, father, crossoverChance, mutationChance, paternalMasks),
                MaternalX = MutateStrand(rng, maternalX, mutationChance, out maternalMasks[Lengths.Length]),
                PaternalSex = MutateStrand(rng, paternalSex, mutationChance, out paternalMasks[Lengths.Length]),
                MaternalMutationMasks = maternalMasks,
                PaternalMutationMasks = paternalMasks,
                PatternSeed = rng.NextDouble(),
                NoseSeed = rng.NextDouble()
            };
        }

        public static Phenotype Evaluate(Genome genome)
        {
            Validate(genome, nameof(genome));
            var shape = Pair(genome, 0);
            var pigment = Pair(genome, 1);
            var coat = Pair(genome, 2);
            var character = Pair(genome, 3);
            var constitution = Pair(genome, 4);
            var controls = Pair(genome, 5);
            var x = new Copies(genome.MaternalX, genome.IsMale ? null : genome.PaternalSex);

            string fur = "hairless";
            if (pigment.On(1)) fur = pigment.On(2) ? "short" : "long";
            int dilution = pigment.ActiveCount(10);
            string baseColor = Pick(DilutedColors, pigment.Code(7, 3))[dilution];
            int coatCode = coat.Code(1, 3);
            int coatPattern = coatCode == 7 ? 3 : coatCode <= 4 ? coatCode + 1 : 1;
            int whiteCount = coat.ActiveCount(7);
            double white = WhiteAmounts[whiteCount];
            if (coatPattern == 4) white = Math.Max(.25, white);
            int markingIndex = ChooseMarking(coat, whiteCount);
            bool suppressedWhite = pigment.On(17);
            if (fur == "hairless")
                markingIndex = 0;
            else if (suppressedWhite)
            {
                baseColor = "chalk";
                coatPattern = 1;
                markingIndex = 0;
            }
            else if (!pigment.On(12))
            {
                markingIndex = 4;
                coatPattern = 1;
            }
            else if (markingIndex == 2)
            {
                if (baseColor is "black" or "charcoal" or "blue")
                {
                    baseColor = "black";
                    coatPattern = 1;
                }
                else markingIndex = 6;
            }
            else if (coatPattern == 1 && baseColor == "black" && markingIndex == 1)
                markingIndex = 8;
            else if (coatPattern == 1 && baseColor is ("black" or "charcoal" or "blue") && markingIndex == 5)
            {
                baseColor = "black";
                markingIndex = coat.On(4) ? 10 : 9;
            }
            else if (coat.On(12) && markingIndex == 0 && baseColor is ("black" or "charcoal" or "blue"))
            {
                if (coatPattern == 1) coatPattern = 6;
                else if (coatPattern == 2) coatPattern = coat.Count(4) == 0 ? 8 : 7;
            }
            int eyeCode = coat.Code(13, 3);
            int leftEye = eyeCode < EyeColors.Length ? eyeCode : 0;
            bool mixedEyeBit = coat.Count(13) == 1 || coat.Count(14) == 1 || coat.Count(15) == 1;
            int rightEye = x.On(1) && mixedEyeBit ? (leftEye + 3) % EyeColors.Length : leftEye;
            if (markingIndex == 4) leftEye = rightEye = Array.IndexOf(EyeColors, "blue");
            string nose = Noses[x.Code(2, 2)];
            bool paternalNoseSpot = genome.IsMale && genome.PaternalSex[0] == '1'
                && dilution > 0 && (nose == "auto" || nose == "rose");
            if (paternalNoseSpot) nose = "spotted";

            int bufferCount = controls.ActiveCount(6);
            int whiteBonus = suppressedWhite ? WhiteIrritabilityBonus[bufferCount] : 0;
            bool slowRecovery = !constitution.On(11);
            bool badWaterSensitive = !constitution.On(13);
            double lifespanFactor = 1 + .10 * controls.Dose(3);
            int vigilanceDose = constitution.Dose(3);
            int reaction = constitution.Baseline(1);
            bool paternalScavenging = genome.IsMale && genome.PaternalSex[1] == '1'
                && character.Dose(6) >= 1;

            return new Phenotype
            {
                Fullness = shape.Measure(1),
                Cheeks = shape.Measure(3),
                Muzzle = shape.Measure(6),
                Eyes = shape.Measure(8),
                Ears = shape.Measure(11),
                EarShape = shape.On(13) ? (shape.On(14) ? "folded" : "slight") : "upright",
                FurLength = fur,
                SkinColor = Pick(SkinColors, pigment.Code(3, 3)),
                BaseColor = baseColor,
                PatchColor = markingIndex == 4 ? baseColor : Pick(PatchColors, pigment.Code(13, 4)),
                CoatPattern = coatPattern,
                Marking = Markings[markingIndex],
                MarkingIndex = markingIndex,
                Bands = Math.Min(1, coat.Measure(4) * StripeFactors[controls.ActiveCount(13)]),
                White = white,
                EyeLeft = EyeColors[leftEye],
                EyeRight = EyeColors[rightEye],
                BrowType = Pick(Brows, coat.Code(16, 3)),
                NosePigment = nose,
                EarPigment = InnerEars[x.Code(4, 2)],
                PatternSeed = genome.PatternSeed,
                NoseSeed = genome.NoseSeed,
                SuppressedWhite = suppressedWhite,
                PaternalNoseSpot = paternalNoseSpot,
                SlowRecovery = slowRecovery,
                BadWaterSensitive = badWaterSensitive,
                BufferCount = bufferCount,
                Affection = character.Baseline(1),
                Irritability = Math.Min(100, character.Baseline(3) + whiteBonus),
                Curiosity = character.Baseline(6),
                Activity = character.Baseline(8),
                Nocturnality = character.Baseline(11),
                HuntingDrive = character.Baseline(13),
                Reaction = reaction,
                NightHuntingReaction = Math.Min(100, reaction + (x.On(6) ? 10 : 0)),
                Vigilance = constitution.Baseline(3),
                SuppressWhiteIrritabilityBonus = whiteBonus,
                NoiseAvoidanceBonus = 10 * Math.Max(0, vigilanceDose) * NoiseFactors[controls.ActiveCount(8)],
                UnfamiliarNightSoundAvoidanceBonus = genome.IsMale && vigilanceDose == 2 ? 8 : 0,
                ClimbingAndWheelEnergyFactor = ActivityEnergyFactors[controls.ActiveCount(11)],
                ScrapHeapSearchBonus = paternalScavenging ? 15 : 0,
                SatietyDrainFactor = 1 + .10 * constitution.Dose(6),
                MaximumEnergyFactor = 1 + .10 * constitution.Dose(8),
                RestRecoveryFactor = slowRecovery ? 0.75 : (constitution.On(12) ? 1.15 : 1),
                BadWaterIllnessFactor = badWaterSensitive ? 1.30 : (constitution.On(14) ? 0.85 : 1),
                JuvenileDays = 7 * (1 - .10 * controls.Dose(1)),
                AdultDays = 14 * lifespanFactor,
                SeniorDays = 7 * lifespanFactor,
                LitterPotential = ExpressLitterPotential(character, constitution, controls),
                PostpartumRecoveryDays = slowRecovery ? 3 : 3 - (constitution.On(12) ? 1 : 0) - (constitution.On(8) ? 1 : 0)
            };
        }

        public static int SampleLitterSize(Random random, Genome genome)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            Validate(genome, nameof(genome));
            int potential = ExpressLitterPotential(Pair(genome, 3), Pair(genome, 4), Pair(genome, 5));
            int totalWeight = 0;
            for (int size = MinimumLitterSize; size <= MaximumLitterSize; size++)
            {
                totalWeight += LitterWeightsByDistance[Math.Abs(size - potential)];
            }

            int draw = random.Next(totalWeight);
            for (int size = MinimumLitterSize; size < MaximumLitterSize; size++)
            {
                draw -= LitterWeightsByDistance[Math.Abs(size - potential)];
                if (draw < 0)
                {
                    return size;
                }
            }
            return MaximumLitterSize;
        }

        private static int ExpressLitterPotential(Copies character, Copies constitution, Copies controls)
        {
            // F07 combines the dominant values of D10, E10 and F10 in that order.
            return MaximumLitterSize -
                (4 * character.Code(10, 1) + 2 * constitution.Code(10, 1) + controls.Code(10, 1));
        }

        public static void Validate(Genome genome) => Validate(genome, nameof(genome));

        private static void Validate(Genome genome, string parameter)
        {
            if (genome == null) throw new ArgumentNullException(parameter);
            if (genome.Maternal?.Length != Lengths.Length || genome.Paternal?.Length != Lengths.Length)
                throw new ArgumentException("A genome must have six maternal and six paternal autosomal copies.", parameter);
            for (int i = 0; i < Lengths.Length; i++)
            {
                CheckStrand(genome.Maternal[i], Lengths[i], parameter);
                CheckStrand(genome.Paternal[i], Lengths[i], parameter);
            }
            CheckStrand(genome.MaternalX, 6, parameter);
            CheckStrand(genome.PaternalSex, genome.IsMale ? 2 : 6, parameter);
            ValidateProbability(genome.PatternSeed, parameter);
            ValidateProbability(genome.NoseSeed, parameter);
        }

        private static void CheckStrand(string strand, int length, string parameter)
        {
            if (strand?.Length != length)
                throw new ArgumentException($"Expected a strand of {length} bits.", parameter);
            foreach (char bit in strand)
            {
                if (bit != '0' && bit != '1')
                    throw new ArgumentException("Every position must be 0 or 1.", parameter);
            }
        }

        private static void ValidateProbability(double value, string parameter)
        {
            if (!(value >= 0 && value <= 1))
                throw new ArgumentOutOfRangeException(parameter, "Expected a finite value from 0 through 1.");
        }

        private static string[] FounderAutosomes(Random rng)
        {
            var strands = new string[Lengths.Length];
            for (int i = 0; i < strands.Length; i++)
                strands[i] = FounderStrand(rng, (char)('A' + i), Lengths[i], Neutral[i], Reserve[i]);
            return strands;
        }

        private static string FounderStrand(Random rng, char group, int length, int neutral = 0, int reserve = 0)
        {
            var bits = new char[length];
            for (int i = 0; i < bits.Length; i++)
            {
                int position = i + 1;
                double chance = position == neutral || position == reserve
                    ? .5 : FounderChance(group, position);
                bits[i] = rng.NextDouble() < chance ? '1' : '0';
            }
            return new string(bits);
        }

        private static double FounderChance(char group, int position)
        {
            // These probabilities apply only to founders; offspring inherit their parents’ bits.
            switch (group, position)
            {
                case ('A', 13): return .08;
                case ('A', 14): return .065;
                case ('B', 1): return .91;
                case ('B', 2): return .64;
                case ('B', 10):
                case ('B', 11): return .12;
                case ('B', 12): return .85;
                case ('B', 17): return .012;
                case ('E', 11): return .84;
                case ('E', 13): return .79;
                case ('D', 10):
                case ('E', 10):
                case ('F', 10): return .5;
                case ('F', 6): return .34;
                case ('F', 7): return .21;
                case ('C', 12): return .16;
                case ('X', 1): return .035;
                case ('Y', 1): return .40;
                case ('Y', 2): return .32;
                default: return .28;
            }
        }

        private static string[] InheritAutosomes(Random rng, Genome parent, double crossoverChance, double mutationChance, int[] masks)
        {
            var strands = new string[Lengths.Length];
            for (int i = 0; i < strands.Length; i++)
            {
                string inherited = Transmit(rng, parent.Maternal[i], parent.Paternal[i], crossoverChance);
                strands[i] = MutateStrand(rng, inherited, mutationChance, out masks[i]);
            }
            return strands;
        }

        private static string Transmit(Random rng, string first, string second, double crossoverChance)
        {
            if (rng.Next(2) == 1)
            {
                string swap = first;
                first = second;
                second = swap;
            }
            if (rng.NextDouble() >= crossoverChance) return first;
            int cut = rng.Next(1, first.Length);
            return first.Substring(0, cut) + second.Substring(cut);
        }

        private static string MutateStrand(Random rng, string strand, double chance, out int mutationMask)
        {
            mutationMask = 0;
            if (chance == 0) return strand;
            var bits = strand.ToCharArray();
            for (int i = 0; i < bits.Length; i++)
            {
                if (rng.NextDouble() < chance)
                {
                    bits[i] = bits[i] == '1' ? '0' : '1';
                    mutationMask |= 1 << i;
                }
            }
            return new string(bits);
        }

        private static int ChooseMarking(Copies coat, int whiteCount)
        {
            int index = coat.Code(9, 3);
            if (index == 4 || index >= MarkingMinWhite.Length || whiteCount < MarkingMinWhite[index])
                index = 0;
            // Spots require the two contributing 1s on one conserved fragment.
            if (index == 0 && coat.Code(1, 5) == 0b01001 && coat.Linked(2, 5))
                return 7;
            return index;
        }

        private static Copies Pair(Genome genome, int group) =>
            new Copies(genome.Maternal[group], genome.Paternal[group]);

        private static T Pick<T>(T[] choices, int code) => code < choices.Length ? choices[code] : choices[0];

        private readonly struct Copies
        {
            private readonly string first;
            private readonly string? second;

            public Copies(string first, string? second)
            {
                this.first = first;
                this.second = second;
            }

            public int Count(int position)
            {
                int i = position - 1;
                return (first[i] == '1' ? 1 : 0) + (second != null && second[i] == '1' ? 1 : 0);
            }

            public bool On(int position) => Count(position) > 0;
            public int Dose(int start) => Count(start + 1) - Count(start);
            public int ActiveCount(int start) => (On(start) ? 1 : 0) + (On(start + 1) ? 1 : 0);
            public double Measure(int start) => .50 + .15 * Dose(start);
            public int Baseline(int start) => 50 + 15 * Dose(start);

            public int Code(int start, int length)
            {
                int code = 0;
                for (int i = start; i < start + length; i++)
                    code = (code << 1) | (On(i) ? 1 : 0);
                return code;
            }

            public bool Linked(int a, int b) =>
                (first[a - 1] == '1' && first[b - 1] == '1')
                || (second != null && second[a - 1] == '1' && second[b - 1] == '1');
        }
    }
}
