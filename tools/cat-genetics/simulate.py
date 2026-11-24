"""Run a single pedigree line with a new outside mate in each generation."""

import argparse
import json
import random
from dataclasses import dataclass
from pathlib import Path

from expression import evaluate


AUTOSOMES = {
    "A": (14, 5, 10),
    "B": (17, 6, None),
    "C": (18, 6, None),
    "D": (14, 5, 10),
    "E": (14, 5, 10),
    "F": (14, 5, 10),
}

# Founder probabilities belong to this experiment, not the game's balance.
FOUNDER_CHANCES = {
    ("A", 13): .08,
    ("A", 14): .065,
    ("B", 12): .85,
    ("B", 1): .91,
    ("B", 2): .64,
    ("E", 11): .84,
    ("E", 13): .79,
    ("F", 6): .34,
    ("F", 7): .21,
    ("C", 12): .16,
    ("X", 1): .035,
    ("Y", 1): .40,
    ("Y", 2): .32,
}


@dataclass(frozen=True)
class Settings:
    seed: int = 20261120
    generations: int = 100
    crossover_chance: float = .35
    mutation_chance: float = .001
    white_allele_chance: float = .012
    dilution_allele_chance: float = .12


def founder_strand(rng, settings, name, length, neutral=None, reserved=None):
    bits = []
    for position in range(1, length + 1):
        chance = FOUNDER_CHANCES.get((name, position), .28)
        if name == "B":
            if position == 17:
                chance = settings.white_allele_chance
            elif position in (10, 11):
                chance = settings.dilution_allele_chance
        if position == neutral or position == reserved:
            chance = .5
        bits.append(str(int(rng.random() < chance)))
    return "".join(bits)


def founder_genome(rng, settings, male):
    return {
        "maternal": [founder_strand(rng, settings, name, *layout) for name, layout in AUTOSOMES.items()],
        "paternal": [founder_strand(rng, settings, name, *layout) for name, layout in AUTOSOMES.items()],
        "maternalX": founder_strand(rng, settings, "X", 6),
        "paternalSex": founder_strand(rng, settings, "Y" if male else "X", 2 if male else 6),
        "patternSeed": rng.random(),
        "noseSeed": rng.random(),
    }


def transmit(rng, copies, chance):
    first = rng.randrange(2)
    start, other = copies[first], copies[1 - first]
    if rng.random() >= chance:
        return start
    cut = rng.randrange(1, len(start))
    return start[:cut] + other[cut:]


def mutate(rng, strand, chance):
    return "".join(
        bit if rng.random() >= chance else ("0" if bit == "1" else "1")
        for bit in strand
    )


def inherit(rng, mother, father, settings):
    def autosomes(parent):
        return [
            mutate(
                rng,
                transmit(rng, (a, b), settings.crossover_chance),
                settings.mutation_chance,
            )
            for a, b in zip(parent["maternal"], parent["paternal"], strict=True)
        ]

    # The father's X and Y are transmitted intact before mutation.
    paternal_sex = rng.choice((father["maternalX"], father["paternalSex"]))
    maternal_x = transmit(rng, (mother["maternalX"], mother["paternalSex"]), settings.crossover_chance)
    return {
        "maternal": autosomes(mother),
        "paternal": autosomes(father),
        "maternalX": mutate(rng, maternal_x, settings.mutation_chance),
        "paternalSex": mutate(rng, paternal_sex, settings.mutation_chance),
        "patternSeed": rng.random(),
        "noseSeed": rng.random(),
    }


def individual(number, index, genome, mother=None, father=None):
    return {
        "id": f"G{number:03}-{index:02}",
        "generation": number,
        "sex": "XY" if len(genome["paternalSex"]) == 2 else "XX",
        "motherId": mother["id"] if mother else None,
        "fatherId": father["id"] if father else None,
        "genome": genome,
        "phenotype": evaluate(genome),
    }


def summarize(cats):
    phenotypes = [cat["phenotype"] for cat in cats]
    return {
        "total": len(cats),
        "males": sum(cat["sex"] == "XY" for cat in cats),
        "hairless": sum(p["fur"] == "hairless" for p in phenotypes),
        "pureWhite": sum(p["whiteCoat"] for p in phenotypes),
        "solidCream": sum(
            p["fur"] != "hairless" and p["appearance"]["baseColor"] == "cream"
            and p["appearance"]["coatPattern"] == 1 and p["appearance"]["marking"] == 0
            for p in phenotypes
        ),
        "van": sum(p["marking"] == "van" for p in phenotypes),
        "spotted": sum(p["marking"] == "spotted" for p in phenotypes),
        "paternalNoseSpot": sum(p["paternalNoseSpot"] for p in phenotypes),
        "slowRecovery": sum(p["slowRecovery"] for p in phenotypes),
        "badWaterSensitive": sum(p["badWaterSensitive"] for p in phenotypes),
        "suppressedWhite": sum(p["whiteCoat"] and p["suppressedWhite"] for p in phenotypes),
        "whiteBuffered": sum(
            p["whiteCoat"] and p["suppressedWhite"] and p["bufferCount"] > 0 for p in phenotypes
        ),
    }


def simulate(settings):
    rng = random.Random(settings.seed)
    current = individual(0, 1, founder_genome(rng, settings, male=True))
    lineage = [current]
    cats = [current]
    for generation in range(settings.generations):
        mate = individual(generation, 2, founder_genome(rng, settings, male=current["sex"] == "XX"))
        mother, father = (current, mate) if current["sex"] == "XX" else (mate, current)
        kitten = inherit(rng, mother["genome"], father["genome"], settings)
        current = individual(generation + 1, 1, kitten, mother, father)
        lineage.append(current)
        cats.extend((mate, current))
    return {
        "seed": settings.seed,
        "settings": {
            "generations": settings.generations,
            "crossoverChance": settings.crossover_chance,
            "mutationChance": settings.mutation_chance,
            "whiteAlleleChance": settings.white_allele_chance,
            "dilutionAlleleChance": settings.dilution_allele_chance,
        },
        "lineage": [cat["id"] for cat in lineage],
        "cats": cats,
        "summary": summarize(lineage),
    }


def main():
    defaults = Settings()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--seed", type=int, default=defaults.seed)
    parser.add_argument("--generations", type=int, default=defaults.generations)
    parser.add_argument("--crossover-chance", type=float, default=defaults.crossover_chance)
    parser.add_argument("--mutation-chance", type=float, default=defaults.mutation_chance)
    parser.add_argument("--white-allele-chance", type=float, default=defaults.white_allele_chance,
                        help="Probability of B17 = 1 in each randomly drawn outside strand.")
    parser.add_argument("--dilution-allele-chance", type=float, default=defaults.dilution_allele_chance,
                        help="Probability of B10 or B11 = 1 in each randomly drawn outside strand.")
    parser.add_argument("--output", type=Path,
                        default=Path(__file__).resolve().parent.parent / "cat-lab" / "genetics" / "study.js")
    args = parser.parse_args()
    output = args.output
    del args.output
    settings = Settings(**vars(args))
    if settings.generations < 1:
        parser.error("Use at least one generation.")
    rates = (settings.crossover_chance, settings.mutation_chance,
             settings.white_allele_chance, settings.dilution_allele_chance)
    if not all(0 <= rate <= 1 for rate in rates):
        parser.error("All probabilities must be between zero and one.")
    study = simulate(settings)
    output.parent.mkdir(parents=True, exist_ok=True)
    data = json.dumps(study, ensure_ascii=True, separators=(",", ":"), allow_nan=False)
    output.write_text(f"window.CatGeneticsStudy = {data};\n", encoding="utf-8")
    print(f"Seed {settings.seed}. G0–G{settings.generations}, "
          f"{len(study['lineage'])} cats in the line, {len(study['cats'])} cats including mates.")
    print(json.dumps(study["summary"]))
    print(output.resolve())


if __name__ == "__main__":
    main()
