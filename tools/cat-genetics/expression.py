"""Decode the mod's phased binary chromosomes into expressed traits."""

from collections.abc import Sequence


SKIN_COLORS = ("rose", "sand", "pearl", "slate", "cocoa")
BASE_COLORS = ("ginger", "black", "chocolate", "blue", "chalk")
PATCH_COLORS = (
    "charcoal", "black", "chocolate", "cinnamon", "blue", "silver",
    "lilac", "fawn", "ginger", "cream", "chalk",
)
EYE_COLORS = ("amber", "gold", "copper", "hazel", "green", "aqua", "blue")
BROWS = ("none", "fine", "bold", "arched", "points")
NOSES = ("auto", "rose", "spotted", "dark")
INNER_EARS = ("auto", "rose", "smoke", "dark")
MARKINGS = ("none", "gloves", "tuxedo", "bib", "point", "van", "harlequin", "spotted", "snowpaws", "inktail", "inkseal")
MARKING_MIN_WHITE = (0, 1, 2, 1, 0, 2, 2)
DILUTED_COLORS = {
    "ginger": ("ginger", "cream", "cream"),
    "black": ("black", "charcoal", "blue"),
    "chocolate": ("chocolate", "cinnamon", "fawn"),
    "blue": ("blue", "lilac", "silver"),
    "chalk": ("chalk", "chalk", "chalk"),
}


def _copies(strands: Sequence[str], position: int) -> int:
    """Read the documented one-based physical position."""
    return sum(strand[position - 1] == "1" for strand in strands)


def _on(strands: Sequence[str], position: int) -> bool:
    return _copies(strands, position) > 0


def _dose(strands: Sequence[str], start: int) -> int:
    return _copies(strands, start + 1) - _copies(strands, start)


def _active_count(strands: Sequence[str], start: int) -> int:
    return int(_on(strands, start)) + int(_on(strands, start + 1))


def _code(strands: Sequence[str], start: int, length: int) -> int:
    value = 0
    for position in range(start, start + length):
        value = (value << 1) | _on(strands, position)
    return value


def _pick(table: Sequence[str], value: int) -> str:
    return table[value] if value < len(table) else table[0]


def _measure(strands: Sequence[str], start: int) -> float:
    return 0.50 + 0.15 * _dose(strands, start)


def _baseline(strands: Sequence[str], start: int) -> int:
    return 50 + 15 * _dose(strands, start)


def _choose_marking(coat: Sequence[str], white_count: int) -> int:
    marking = _code(coat, 9, 3)
    if marking == 4 or marking >= len(MARKING_MIN_WHITE) or white_count < MARKING_MIN_WHITE[marking]:
        marking = 0
    # The expressed code determines which bits are on, and the phase check
    # determines whether the two required 1s sit on a conserved fragment.
    if marking == 0 and _code(coat, 1, 5) == 0b01001:
        if any(strand[1] == strand[4] == "1" for strand in coat):
            marking = 7
    return marking


def _eye_colors(coat: Sequence[str], x: Sequence[str]) -> tuple[str, str]:
    left = _pick(EYE_COLORS, _code(coat, 13, 3))
    has_mixed_bit = any(_copies(coat, position) == 1 for position in range(13, 16))
    if _on(x, 1) and has_mixed_bit:
        return left, EYE_COLORS[(EYE_COLORS.index(left) + 3) % len(EYE_COLORS)]
    return left, left


def _build_appearance(genome: dict, chromosomes: tuple, x: tuple, male: bool) -> tuple:
    shape, pigment, coat = chromosomes[:3]
    if not _on(pigment, 1):
        fur = "hairless"
    else:
        fur = "short" if _on(pigment, 2) else "long"

    dilution = _active_count(pigment, 10)
    base_color = DILUTED_COLORS[_pick(BASE_COLORS, _code(pigment, 7, 3))][dilution]
    coat_mode = _code(coat, 1, 3)
    coat_pattern = 3 if coat_mode == 7 else coat_mode + 1 if coat_mode <= 4 else 1
    white_count = _active_count(coat, 7)
    white = (0.0, 0.45, 0.85)[white_count]
    if coat_pattern == 4:
        white = max(white, 0.25)
    marking = _choose_marking(coat, white_count)
    suppressed_white = _on(pigment, 17)
    if fur == "hairless":
        marking = 0
    elif suppressed_white:
        base_color = "chalk"
        coat_pattern = 1
        marking = 0

    elif not _on(pigment, 12):
        marking, coat_pattern = 4, 1
    elif marking == 2:
        if base_color in ("black", "charcoal", "blue"):
            base_color, coat_pattern = "black", 1
        else:
            marking = 6
    elif coat_pattern == 1 and base_color == "black" and marking == 1:
        marking = 8
    elif coat_pattern == 1 and base_color in ("black", "charcoal", "blue") and marking == 5:
        base_color = "black"
        marking = 10 if _on(coat, 4) else 9
    elif _on(coat, 12) and marking == 0 and base_color in ("black", "charcoal", "blue"):
        if coat_pattern == 1:
            coat_pattern = 6
        elif coat_pattern == 2:
            coat_pattern = 8 if _copies(coat, 4) == 0 else 7

    eye_left, eye_right = _eye_colors(coat, x)
    if marking == 4:
        eye_left = eye_right = "blue"
    nose = NOSES[_code(x, 2, 2)]
    paternal_nose_spot = (
        male and genome["paternalSex"][0] == "1" and dilution > 0
        and nose in ("auto", "rose")
    )
    if paternal_nose_spot:
        nose = "spotted"

    ear_shape = "upright"
    if _on(shape, 13):
        ear_shape = "folded" if _on(shape, 14) else "slight"

    stripe_factor = (1.0, 1.15, 1.30)[_active_count(chromosomes[5], 13)]
    bands = min(_measure(coat, 4) * stripe_factor, 1.0)
    appearance = {
        "fullness": _measure(shape, 1),
        "cheeks": _measure(shape, 3),
        "muzzle": _measure(shape, 6),
        "eyes": _measure(shape, 8),
        "ears": _measure(shape, 11),
        "earShape": ear_shape,
        "furLength": fur,
        "skinColor": _pick(SKIN_COLORS, _code(pigment, 3, 3)),
        "baseColor": base_color,
        "patchColor": base_color if marking == 4 else _pick(PATCH_COLORS, _code(pigment, 13, 4)),
        "coatPattern": coat_pattern,
        "marking": marking,
        "bands": bands,
        "white": white,
        "eyeMode": "odd" if eye_left != eye_right else "matched",
        "eyeLeft": eye_left,
        "eyeRight": eye_right,
        "browType": _pick(BROWS, _code(coat, 16, 3)),
        "nosePigment": nose,
        "earPigment": INNER_EARS[_code(x, 4, 2)],
        "patternSeed": genome.get("patternSeed", 0.36),
        "noseSeed": genome.get("noseSeed", 0.42),
    }
    return appearance, suppressed_white, paternal_nose_spot


def evaluate(genome: dict) -> dict:
    """Evaluate one individual without changing either parental contribution."""
    chromosomes = tuple(zip(genome["maternal"], genome["paternal"], strict=True))
    _, _, _, character, constitution, controls = chromosomes
    sex_chromosome = genome["paternalSex"]
    male = len(sex_chromosome) == 2
    x = (genome["maternalX"],) if male else (genome["maternalX"], sex_chromosome)
    appearance, suppressed_white, paternal_nose_spot = _build_appearance(
        genome, chromosomes, x, male,
    )

    buffer_count = _active_count(controls, 6)
    irritability_bonus = (30, 15, 0)[buffer_count] if suppressed_white else 0
    irritability = min(100, _baseline(character, 3) + irritability_bonus)
    slow_recovery = not _on(constitution, 11)
    bad_water_sensitive = not _on(constitution, 13)
    recovery_factor = 1.15 if _on(constitution, 12) else 1.0
    bad_water_factor = 0.85 if _on(constitution, 14) else 1.0
    lifespan_factor = 1 + 0.10 * _dose(controls, 3)
    noise_factor = (1.0, 0.75, 0.50)[_active_count(controls, 8)]
    reaction = _baseline(constitution, 1)
    paternal_scavenging = male and sex_chromosome[1] == "1" and _dose(character, 6) >= 1

    traits = {
        "affection": _baseline(character, 1),
        "irritability": irritability,
        "curiosity": _baseline(character, 6),
        "activity": _baseline(character, 8),
        "nocturnality": _baseline(character, 11),
        "huntingDrive": _baseline(character, 13),
        "reaction": reaction,
        "nightHuntingReaction": min(100, reaction + 10 * _on(x, 6)),
        "vigilance": _baseline(constitution, 3),
    }
    modifiers = {
        "suppressWhiteIrritabilityBonus": irritability_bonus,
        "noiseAvoidanceBonus": 10 * max(0, _dose(constitution, 3)) * noise_factor,
        "unfamiliarNightSoundAvoidanceBonus": 8 if male and _dose(constitution, 3) == 2 else 0,
        "climbingAndWheelEnergyFactor": (1.0, 0.90, 0.80)[_active_count(controls, 11)],
        "scrapHeapSearchBonus": 15 if paternal_scavenging else 0,
    }
    physique = {
        "satietyDrainFactor": 1 + 0.10 * _dose(constitution, 6),
        "maximumEnergyFactor": 1 + 0.10 * _dose(constitution, 8),
        "restRecoveryFactor": 0.75 if slow_recovery else recovery_factor,
        "postpartumRecoveryDays": 3 if slow_recovery else 3 - int(_on(constitution, 12)) - int(_on(constitution, 8)),
        "badWaterIllnessFactor": 1.30 if bad_water_sensitive else bad_water_factor,
        "juvenileDays": 7 * (1 - 0.10 * _dose(controls, 1)),
        "adultDays": 14 * lifespan_factor,
        "seniorDays": 7 * lifespan_factor,
    }
    fur = appearance["furLength"]
    return {
        "fur": fur,
        "marking": MARKINGS[appearance["marking"]],
        "suppressedWhite": suppressed_white,
        "whiteCoat": (
            fur != "hairless" and appearance["baseColor"] == "chalk"
            and appearance["coatPattern"] == 1 and appearance["marking"] == 0
        ),
        "paternalNoseSpot": paternal_nose_spot,
        "slowRecovery": slow_recovery,
        "badWaterSensitive": bad_water_sensitive,
        "irritability": irritability,
        "bufferCount": buffer_count,
        "appearance": appearance,
        "traits": traits,
        "physique": physique,
        "modifiers": modifiers,
    }
