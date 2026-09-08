#!/usr/bin/env python3
"""Analyze collisions for alternate rotations of registered ride zero blocks.

The codec and registered sequence constants are loaded from
``ride-encoding-hypothesis.py`` so this tool does not maintain a second copy of
that formula.  A baseline row is one canonical sequence/count pair in the
selected inclusive count range.

By default this prints all 9 zero blocks at all 8 tested rotations, sorted by
collision count.  Use both ``--sequence`` and ``--rotation`` for a detailed
single-candidate report.
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import sys
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Sequence


# The hyphenated filename is not a normal import name.  Load it by path and
# register it before execution so dataclasses and future imports see a normal
# module entry.  This intentionally reuses its codec/constants.
_HYPOTHESIS_PATH = Path(__file__).with_name("ride-encoding-hypothesis.py")
_spec = importlib.util.spec_from_file_location("ride_encoding_hypothesis", _HYPOTHESIS_PATH)
if _spec is None or _spec.loader is None:  # pragma: no cover - installation error
    raise ImportError(f"cannot load {_HYPOTHESIS_PATH}")
_hypothesis = importlib.util.module_from_spec(_spec)
sys.modules[_spec.name] = _hypothesis
_spec.loader.exec_module(_hypothesis)

SEQUENCES = _hypothesis.SEQUENCES
encode = _hypothesis.encode

_SEQUENCE_BY_NAME = {sequence.name: sequence for sequence in SEQUENCES}
_SEQUENCE_ORDER = {sequence.name: index for index, sequence in enumerate(SEQUENCES)}


@dataclass(frozen=True)
class Collision:
    candidate_sequence: str
    candidate_count: int
    candidate_block: int
    registered_sequence: str
    registered_count: int


@dataclass(frozen=True)
class SelfCollision:
    candidate_block: int
    candidate_counts: tuple[int, ...]


@dataclass(frozen=True)
class CandidateAnalysis:
    sequence: object
    rotation: int
    min_count: int
    max_count: int
    collisions: tuple[Collision, ...]
    self_collisions: tuple[SelfCollision, ...]

    @property
    def total_collisions(self) -> int:
        """Number of candidate/registered count pairs with equal block values."""
        return len(self.collisions)

    @property
    def source_collisions(self) -> int:
        return sum(
            collision.registered_sequence == self.sequence.name
            for collision in self.collisions
        )

    @property
    def other_collisions(self) -> int:
        return self.total_collisions - self.source_collisions

    @property
    def same_count_collisions(self) -> int:
        return sum(
            collision.candidate_count == collision.registered_count
            for collision in self.collisions
        )

    @property
    def cross_count_collisions(self) -> int:
        return self.total_collisions - self.same_count_collisions

    @property
    def self_collision_pairs(self) -> int:
        return sum(len(item.candidate_counts) * (len(item.candidate_counts) - 1) // 2
                   for item in self.self_collisions)

    @property
    def breakdown(self) -> dict[str, int]:
        counts = Counter(collision.registered_sequence for collision in self.collisions)
        return {sequence.name: counts[sequence.name] for sequence in SEQUENCES}


def _resolve_sequence(sequence: str | object) -> object:
    if isinstance(sequence, str):
        try:
            return _SEQUENCE_BY_NAME[sequence]
        except KeyError as error:
            choices = ", ".join(_SEQUENCE_BY_NAME)
            raise ValueError(f"unknown sequence {sequence!r}; choose from {choices}") from error
    if getattr(sequence, "name", None) in _SEQUENCE_BY_NAME:
        return _SEQUENCE_BY_NAME[sequence.name]
    raise ValueError(f"unknown sequence object {sequence!r}")


def validate_range(min_count: int, max_count: int) -> None:
    if not 0 <= min_count <= 0x1FF:
        raise ValueError(f"min-count must be in [0, 511], got {min_count}")
    if not 0 <= max_count <= 0x1FF:
        raise ValueError(f"max-count must be in [0, 511], got {max_count}")
    if min_count > max_count:
        raise ValueError(f"min-count must not exceed max-count ({min_count} > {max_count})")


def analyze_candidate(
    sequence: str | object,
    rotation: int,
    min_count: int = 0,
    max_count: int = 500,
) -> CandidateAnalysis:
    """Return exact-value baseline and candidate-self collision results."""
    candidate = _resolve_sequence(sequence)
    if not 0 <= rotation <= 7:
        raise ValueError(f"rotation must be in [0, 7], got {rotation}")
    validate_range(min_count, max_count)

    counts = range(min_count, max_count + 1)
    baseline: dict[int, list[tuple[str, int]]] = defaultdict(list)
    for registered in SEQUENCES:
        for registered_count in counts:
            baseline[registered.encode(registered_count)].append(
                (registered.name, registered_count)
            )

    candidate_blocks: dict[int, list[int]] = defaultdict(list)
    collisions: list[Collision] = []
    for candidate_count in counts:
        candidate_block = encode(candidate.zero_block, rotation, candidate_count)
        candidate_blocks[candidate_block].append(candidate_count)
        for registered_name, registered_count in baseline.get(candidate_block, ()):
            collisions.append(
                Collision(
                    candidate_sequence=candidate.name,
                    candidate_count=candidate_count,
                    candidate_block=candidate_block,
                    registered_sequence=registered_name,
                    registered_count=registered_count,
                )
            )

    self_collisions = tuple(
        SelfCollision(candidate_block=block, candidate_counts=tuple(candidate_counts))
        for block, candidate_counts in sorted(candidate_blocks.items())
        if len(candidate_counts) > 1
    )
    collisions.sort(
        key=lambda collision: (
            collision.candidate_count,
            collision.candidate_block,
            _SEQUENCE_ORDER[collision.registered_sequence],
            collision.registered_count,
        )
    )
    return CandidateAnalysis(
        sequence=candidate,
        rotation=rotation,
        min_count=min_count,
        max_count=max_count,
        collisions=tuple(collisions),
        self_collisions=self_collisions,
    )


def all_analyses(
    min_count: int = 0,
    max_count: int = 500,
    sequence: str | None = None,
    rotation: int | None = None,
) -> list[CandidateAnalysis]:
    """Analyze the selected fan-out, sorted by total then stable tie-breakers."""
    validate_range(min_count, max_count)
    if rotation is not None and not 0 <= rotation <= 7:
        raise ValueError(f"rotation must be in [0, 7], got {rotation}")
    selected_sequences = (
        [_resolve_sequence(sequence)] if sequence is not None else list(SEQUENCES)
    )
    rotations = [rotation] if rotation is not None else list(range(8))
    analyses = [
        analyze_candidate(candidate, tested_rotation, min_count, max_count)
        for candidate in selected_sequences
        for tested_rotation in rotations
    ]
    analyses.sort(key=lambda item: (
        item.total_collisions,
        item.sequence.name,
        item.rotation,
    ))
    return analyses


def _breakdown_text(analysis: CandidateAnalysis) -> str:
    return ",".join(
        f"{name}={count}" for name, count in analysis.breakdown.items()
    )


def _summary_line(analysis: CandidateAnalysis) -> str:
    return (
        f"{analysis.sequence.name:8} zero={analysis.sequence.zero_block:08X} "
        f"canonical={analysis.sequence.rotation} tested={analysis.rotation} "
        f"total={analysis.total_collisions} source={analysis.source_collisions} "
        f"other={analysis.other_collisions} same-count={analysis.same_count_collisions} "
        f"cross-count={analysis.cross_count_collisions} "
        f"self-groups={len(analysis.self_collisions)} self-pairs={analysis.self_collision_pairs} "
        f"breakdown[{_breakdown_text(analysis)}]"
    )


def _print_details(analysis: CandidateAnalysis) -> None:
    print(
        f"Candidate: sequence={analysis.sequence.name} "
        f"zeroBlock={analysis.sequence.zero_block:08X} "
        f"canonicalRotation={analysis.sequence.rotation} "
        f"testedRotation={analysis.rotation}"
    )
    print(
        f"Result: total collisions={analysis.total_collisions} "
        f"source={analysis.source_collisions} other={analysis.other_collisions} "
        f"same-count={analysis.same_count_collisions} "
        f"cross-count={analysis.cross_count_collisions} "
        f"candidate self-collision groups={len(analysis.self_collisions)} "
        f"pairs={analysis.self_collision_pairs}"
    )
    print(f"Breakdown by registered family: {_breakdown_text(analysis)}")
    if analysis.self_collisions:
        print("Candidate self-collisions (same candidate block at multiple counts):")
        for item in analysis.self_collisions:
            counts = ",".join(str(count) for count in item.candidate_counts)
            print(f"  block={item.candidate_block:08X} candidateCounts={counts}")
    else:
        print("Candidate self-collisions: none")
    print("Baseline collision rows (exact 32-bit block equality):")
    if not analysis.collisions:
        print("  none")
    for collision in analysis.collisions:
        print(
            f"  candidate sequence={collision.candidate_sequence} "
            f"count={collision.candidate_count} "
            f"block={collision.candidate_block:08X} "
            f"registered sequence={collision.registered_sequence} "
            f"count={collision.registered_count}"
        )


def _json_analysis(analysis: CandidateAnalysis) -> dict[str, object]:
    return {
        "sequence": analysis.sequence.name,
        "zero_block": f"{analysis.sequence.zero_block:08X}",
        "canonical_rotation": analysis.sequence.rotation,
        "tested_rotation": analysis.rotation,
        "min_count": analysis.min_count,
        "max_count": analysis.max_count,
        "total_collisions": analysis.total_collisions,
        "source_collisions": analysis.source_collisions,
        "other_collisions": analysis.other_collisions,
        "same_count_collisions": analysis.same_count_collisions,
        "cross_count_collisions": analysis.cross_count_collisions,
        "breakdown": analysis.breakdown,
        "self_collision_groups": [
            {
                "block": f"{item.candidate_block:08X}",
                "candidate_counts": list(item.candidate_counts),
            }
            for item in analysis.self_collisions
        ],
        "self_collision_pairs": analysis.self_collision_pairs,
        "collisions": [
            {
                "candidate_sequence": item.candidate_sequence,
                "candidate_count": item.candidate_count,
                "candidate_block": f"{item.candidate_block:08X}",
                "registered_sequence": item.registered_sequence,
                "registered_count": item.registered_count,
            }
            for item in analysis.collisions
        ],
    }


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Compare every selected zeroBlock/rotation candidate with the "
            "canonical registered sequences using exact 32-bit block values."
        ),
        epilog=(
            "Examples: %(prog)s; %(prog)s --sequence mercury --rotation 0; "
            "%(prog)s --full-range --sequence mercury --rotation 0 --json"
        ),
    )
    parser.add_argument("--sequence", choices=tuple(_SEQUENCE_BY_NAME),
                        help="candidate zero block family (omit for all 9)")
    parser.add_argument("--rotation", type=int, choices=range(8),
                        metavar="0..7", help="tested rotation (omit for all 8)")
    parser.add_argument("--min-count", type=int, default=0, metavar="N",
                        help="inclusive count lower bound (default: 0)")
    parser.add_argument("--max-count", type=int, default=500, metavar="N",
                        help="inclusive count upper bound (default: 500)")
    parser.add_argument("--full-range", action="store_true",
                        help="use the complete 9-bit inclusive range 0..511")
    parser.add_argument("--details", action="store_true",
                        help="print detailed collision rows for every selected candidate")
    parser.add_argument("--json", action="store_true",
                        help="emit machine-readable JSON instead of human-readable text")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    if args.full_range and args.max_count != 500:
        parser.error("--full-range cannot be combined with --max-count other than 500")
    max_count = 511 if args.full_range else args.max_count
    try:
        analyses = all_analyses(
            min_count=args.min_count,
            max_count=max_count,
            sequence=args.sequence,
            rotation=args.rotation,
        )
    except ValueError as error:
        parser.error(str(error))

    detailed = args.details or (args.sequence is not None and args.rotation is not None)
    if args.json:
        payload = {
            "semantics": {
                "baseline_match": "exact 32-bit candidate block == registered block",
                "total_collisions": "candidate/registered count pairs, including source family",
                "source_collisions": "registered family equals candidate sequence",
                "other_collisions": "registered family differs from candidate sequence",
                "count_semantics": "candidate and registered counts are independent; differing counts are cross-count collisions",
                "self_collisions": "duplicate candidate block across candidate counts; excluded from total",
            },
            "range": {"min_count": args.min_count, "max_count": max_count},
            "candidates": [_json_analysis(analysis) for analysis in analyses],
        }
        print(json.dumps(payload, indent=2, sort_keys=True))
        return 0

    print(
        "Collision semantics: exact 32-bit block equality; total is the number "
        "of candidate/registered count pairs, including cross-count pairs."
    )
    print(
        "Candidate and registered counts are independent; same-count and "
        "cross-count totals are reported. Source means the registered family "
        "is the candidate sequence; other means a different registered family. "
        "Candidate self-collisions are duplicate candidate blocks across counts "
        "and are excluded from total."
    )
    print(f"Count range: {args.min_count}..{max_count} inclusive ({max_count - args.min_count + 1} values)")
    if detailed:
        for index, analysis in enumerate(analyses):
            if index:
                print()
            _print_details(analysis)
    else:
        print("Sorted candidates: total, then sequence name, then tested rotation")
        print("candidate  zeroBlock  canonical tested total source other self-groups self-pairs breakdown")
        for analysis in analyses:
            print(_summary_line(analysis))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
