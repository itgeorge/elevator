#!/usr/bin/env python3
"""Reproduce the generalized ride-encoding hypotheses from the 2026-07-21 dumps.

This is intentionally an exploration tool, not production encoding code. It:
- expresses every registered sequence as zero_block XOR counter_delta(rides, rotation=4),
- expresses unregistered candidates B/C and registered Jupiter as the same algorithm with rotation=0,
- checks trusted observations and the independent Candidate-B 8B12CB72 dump,
- checks self/cross-sequence collisions over 0..500 and the full 9-bit 0..511 range.
"""

from dataclasses import dataclass


def rol8(value: int, rotation: int) -> int:
    rotation &= 7
    value &= 0xFF
    if rotation == 0:
        return value
    return ((value << rotation) | (value >> (8 - rotation))) & 0xFF


def counter_delta(rides: int, rotation: int) -> int:
    """Encode a 9-bit counter independently of its sequence's zero block.

    Let r be the low counter byte and h its ninth bit. The fourth-byte payload
    is r rotated by the sequence layout, with h folded into the vacated bit.
    Its bit 3 is duplicated into byte 1 as the F3 toggle; h is duplicated into
    byte 2; and r is stored directly in byte 3.
    """
    if not 0 <= rides <= 0x1FF:
        raise ValueError(f"rides must be in [0, 511], got {rides}")

    r = rides & 0xFF
    h = (rides >> 8) & 1
    p = rol8(r, rotation) ^ (h << rotation)

    byte0 = 0xF3 if p & 0x08 else 0x00
    byte1 = h
    return (byte0 << 24) | (byte1 << 16) | (r << 8) | p


def encode(zero_block: int, rotation: int, rides: int) -> int:
    return zero_block ^ counter_delta(rides, rotation)


def infer_zero(block: int, rotation: int, rides: int) -> int:
    return block ^ counter_delta(rides, rotation)


@dataclass(frozen=True)
class SequenceHypothesis:
    name: str
    zero_block: int
    rotation: int

    def encode(self, rides: int) -> int:
        return encode(self.zero_block, self.rotation, rides)


SEQUENCES = (
    SequenceHypothesis("mercury", 0xCCC749CC, 4),
    SequenceHypothesis("venus", 0x48C74948, 4),
    SequenceHypothesis("mars", 0x4EC7494E, 4),
    SequenceHypothesis("earth", 0x18121218, 4),
    SequenceHypothesis("pluto", 0x1F12121F, 4),
    SequenceHypothesis("jupiter", 0x8C124980, 0),
    SequenceHypothesis("saturn", 0x8B1249F0, 0),
    SequenceHypothesis("candidate-c", 0x891249D0, 0),
)

# Earth and Pluto are hardware-validated through the corrected 256/384 boundaries.

# Independent transcription of the current production family model. This is
# used to prove equivalence over every registered value, not just samples.
PRODUCTION_FAMILIES = {
    "mercury": (
        (0, 127, 0xCCC7, 0x0000, 0),
        (128, 255, 0x3FC7, 0x8008, 128),
        (256, 383, 0xCCC6, 0x0010, 256),
        (384, 500, 0x3FC6, 0x8018, 384),
    ),
    "venus": (
        (0, 127, 0x48C7, 0x0084, 0),
        (128, 255, 0xBBC7, 0x808C, 128),
        (256, 383, 0x48C6, 0x0094, 256),
        (384, 500, 0xBBC6, 0x809C, 384),
    ),
    "mars": (
        (0, 127, 0x4EC7, 0x0082, 0),
        (128, 255, 0xBDC7, 0x808A, 128),
        (256, 383, 0x4EC6, 0x0092, 256),
        (384, 500, 0xBDC6, 0x809A, 384),
    ),
    "earth": (
        (0, 127, 0x1812, 0x5BD4, 0),
        (128, 255, 0xEB12, 0xDBDC, 128),
        (256, 383, 0x1813, 0x5BC4, 256),
        (384, 500, 0xEB13, 0xDBCC, 384),
    ),
    "pluto": (
        (0, 127, 0x1F12, 0x5BD3, 0),
        (128, 255, 0xEC12, 0xDBDB, 128),
        (256, 383, 0x1F13, 0x5BC3, 256),
        (384, 500, 0xEC13, 0xDBCB, 384),
    ),
}


def production_base_low(m: int) -> int:
    g = m >> 4
    o = m & 0x0F
    high_byte = (((g + 4) & 7) << 4) | (o ^ 9)
    low_byte = ((o ^ 0x0C) << 4) | (g + (0x0C if g < 4 else 4))
    return (high_byte << 8) | low_byte


def production_encode(sequence_name: str, rides: int) -> int:
    for minimum, maximum, high16, xor_value, base in PRODUCTION_FAMILIES[sequence_name]:
        if minimum <= rides <= maximum:
            return (high16 << 16) | (production_base_low(rides - base) ^ xor_value)
    raise ValueError(f"{sequence_name} does not register rides={rides}")


OBSERVATIONS = (
    ("mercury zero", "mercury", 0, 0xCCC749CC),
    ("mercury 500", "mercury", 500, 0x3FC6BD93),
    ("venus 500", "venus", 500, 0xBBC6BD17),
    ("mars 500", "mars", 500, 0xBDC6BD11),
    ("earth 0", "earth", 0, 0x18121218),
    ("earth 128", "earth", 128, 0xEB129210),
    ("earth 255", "earth", 255, 0xEB12EDE7),
    ("earth 256 validated start", "earth", 256, 0x18131208),
    ("earth 383 validated post-ride", "earth", 383, 0x18136DFF),
    ("earth 384 validated start", "earth", 384, 0xEB139200),
    ("pluto 50", "pluto", 50, 0x1F12203C),
    ("pluto 128", "pluto", 128, 0xEC129217),
    ("pluto 255", "pluto", 255, 0xEC12EDE0),
    ("pluto 256 validated start", "pluto", 256, 0x1F13120F),
    ("pluto 383 validated post-ride", "pluto", 383, 0x1F136DF8),
    ("pluto 384 validated start", "pluto", 384, 0xEC139207),
    ("saturn anchor", "saturn", 47, 0x781266DF),
    ("saturn decrement", "saturn", 46, 0x781267DE),
    ("saturn independent dump", "saturn", 130, 0x8B12CB72),
    ("saturn 128 boundary", "saturn", 128, 0x8B12C970),
    ("saturn 127 post-ride", "saturn", 127, 0x7812368F),
    ("saturn 256 boundary", "saturn", 256, 0x8B1349F1),
    ("saturn 255 post-ride", "saturn", 255, 0x7812B60F),
    ("saturn 384 boundary", "saturn", 384, 0x8B13C971),
    ("saturn 383 post-ride", "saturn", 383, 0x7813368E),
    ("saturn 8 boundary", "saturn", 8, 0x781241F8),
    ("saturn 7 post-ride", "saturn", 7, 0x8B124EF7),
    ("saturn 1 pre-zero", "saturn", 1, 0x8B1248F1),
    ("saturn zero", "saturn", 0, 0x8B1249F0),
    ("candidate C anchor", "candidate-c", 107, 0x7A1222BB),
    ("candidate C decrement", "candidate-c", 106, 0x7A1223BA),
    ("jupiter anchor", "jupiter", 57, 0x7F1270B9),
    ("jupiter decrement", "jupiter", 56, 0x7F1271B8),
    # Historical EBFE states decode as 9-bit counts despite bad CSV ride labels.
    ("jupiter historical 261", "jupiter", 261, 0x8C134C84),
    ("jupiter historical 256", "jupiter", 256, 0x8C134981),
    ("jupiter historical 255", "jupiter", 255, 0x7F12B67F),
    ("jupiter historical 247", "jupiter", 247, 0x8C12BE77),
    ("jupiter historical 240", "jupiter", 240, 0x8C12B970),
    ("jupiter historical 238", "jupiter", 238, 0x7F12A76E),
)


def assert_production_equivalence() -> None:
    by_name = {sequence.name: sequence for sequence in SEQUENCES}
    for name, families in PRODUCTION_FAMILIES.items():
        sequence = by_name[name]
        minimum = min(family[0] for family in families)
        maximum = max(family[1] for family in families)
        for rides in range(minimum, maximum + 1):
            generalized = sequence.encode(rides)
            production = production_encode(name, rides)
            if generalized != production:
                raise AssertionError(
                    f"production mismatch: {name}/{rides}, generalized "
                    f"{generalized:08X}, production {production:08X}"
                )


def assert_observations() -> None:
    by_name = {sequence.name: sequence for sequence in SEQUENCES}
    for label, sequence_name, rides, expected in OBSERVATIONS:
        actual = by_name[sequence_name].encode(rides)
        if actual != expected:
            raise AssertionError(
                f"{label}: rides={rides}, expected {expected:08X}, got {actual:08X}"
            )


def assert_candidate_rotations() -> None:
    """Independent points select rotation 0 uniquely among rotations 0..7."""
    point_sets = {
        "saturn anchor/independent dump": ((47, 0x781266DF), (130, 0x8B12CB72)),
        "saturn decrement": ((47, 0x781266DF), (46, 0x781267DE)),
        "candidate C decrement": ((107, 0x7A1222BB), (106, 0x7A1223BA)),
        "jupiter decrement": ((57, 0x7F1270B9), (56, 0x7F1271B8)),
    }
    for label, points in point_sets.items():
        matching_rotations = []
        for rotation in range(8):
            inferred = {infer_zero(block, rotation, rides) for rides, block in points}
            if len(inferred) == 1:
                matching_rotations.append(rotation)
        if matching_rotations != [0]:
            raise AssertionError(f"{label}: expected rotation [0], got {matching_rotations}")


def assert_no_collisions(max_rides: int) -> None:
    seen: dict[int, tuple[str, int]] = {}
    for sequence in SEQUENCES:
        own = set()
        for rides in range(max_rides + 1):
            block = sequence.encode(rides)
            if block in own:
                raise AssertionError(
                    f"self-collision: {sequence.name} rides={rides}, block={block:08X}"
                )
            own.add(block)
            if block in seen:
                other = seen[block]
                raise AssertionError(
                    f"cross-collision: {sequence.name}/{rides} and "
                    f"{other[0]}/{other[1]} both encode as {block:08X}"
                )
            seen[block] = (sequence.name, rides)

    expected = len(SEQUENCES) * (max_rides + 1)
    if len(seen) != expected:
        raise AssertionError(f"expected {expected} distinct blocks, got {len(seen)}")


def main() -> None:
    assert_production_equivalence()
    assert_observations()
    assert_candidate_rotations()
    assert_no_collisions(500)
    assert_no_collisions(511)

    print("PASS: generalized ride-encoding hypothesis")
    print("  every currently registered encoding matched over its full range")
    print(f"  trusted/independent observations matched: {len(OBSERVATIONS)}")
    print("  B/C/Jupiter independent point pairs uniquely select rotation 0")
    print(f"  sequences checked: {len(SEQUENCES)}")
    print("  no self- or cross-sequence collisions over 0..500 or 0..511")
    print()
    print("Corrected/high-value predictions (hardware-unvalidated where noted):")
    by_name = {sequence.name: sequence for sequence in SEQUENCES}
    for name in (
        "earth",
        "pluto",
        "saturn",
        "candidate-c",
        "jupiter",
    ):
        sequence = by_name[name]
        values = " ".join(
            f"{rides}={sequence.encode(rides):08X}" for rides in (0, 1, 128, 255, 256, 500)
        )
        print(f"  {name:20} {values}")


if __name__ == "__main__":
    main()
