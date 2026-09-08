#!/usr/bin/env python3
"""Self-tests for ride-rotation-collisions.py."""

import importlib.util
import json
import subprocess
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parent
SCRIPT = ROOT / "ride-rotation-collisions.py"
spec = importlib.util.spec_from_file_location("ride_rotation_collisions", SCRIPT)
assert spec is not None and spec.loader is not None
collisions = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = collisions
spec.loader.exec_module(collisions)


class RideRotationCollisionTests(unittest.TestCase):
    def test_experimental_mercury_rotation_zero_facts(self):
        analysis = collisions.analyze_candidate("mercury", 0, max_count=511)

        candidate_256 = collisions.encode(analysis.sequence.zero_block, 0, 256)
        self.assertEqual(candidate_256, 0xCCC649CD)
        self.assertFalse(any(row.candidate_count == 256 for row in analysis.collisions))

        mercury_pairs = {
            (row.candidate_count, row.registered_count)
            for row in analysis.collisions
            if row.registered_sequence == "mercury"
        }
        self.assertIn((0, 0), mercury_pairs)
        self.assertIn((255, 255), mercury_pairs)
        self.assertEqual(analysis.total_collisions, 32)
        self.assertEqual(analysis.source_collisions, 32)
        self.assertEqual(analysis.other_collisions, 0)
        self.assertEqual(analysis.breakdown["mercury"], 32)
        self.assertEqual(analysis.self_collisions, ())

    def test_canonical_rotations_fully_overlap_registered_sequences(self):
        for sequence in collisions.SEQUENCES:
            analysis = collisions.analyze_candidate(
                sequence.name, sequence.rotation, max_count=511
            )
            self.assertEqual(analysis.total_collisions, 512, sequence.name)
            self.assertEqual(analysis.source_collisions, 512, sequence.name)
            self.assertEqual(analysis.other_collisions, 0, sequence.name)
            self.assertEqual(analysis.breakdown[sequence.name], 512, sequence.name)
            self.assertEqual(len(analysis.collisions), 512, sequence.name)
            self.assertTrue(
                all(
                    row.candidate_count == row.registered_count
                    and row.registered_sequence == sequence.name
                    for row in analysis.collisions
                ),
                sequence.name,
            )

    def test_range_and_sorting(self):
        default = collisions.analyze_candidate("mercury", 0)
        full = collisions.analyze_candidate("mercury", 0, max_count=511)
        self.assertEqual(default.max_count, 500)
        self.assertEqual(default.total_collisions, 31)
        self.assertEqual(full.total_collisions, 32)

        fanout = collisions.all_analyses()
        self.assertEqual(len(fanout), 9 * 8)
        totals = [analysis.total_collisions for analysis in fanout]
        self.assertEqual(totals, sorted(totals))
        self.assertEqual(
            [(analysis.sequence.name, analysis.rotation) for analysis in fanout[:4]],
            [("earth", 1), ("earth", 3), ("earth", 5), ("earth", 7)],
        )

    def test_cli_json_single_candidate(self):
        completed = subprocess.run(
            [
                sys.executable,
                str(SCRIPT),
                "--sequence",
                "mercury",
                "--rotation",
                "0",
                "--full-range",
                "--json",
            ],
            check=True,
            capture_output=True,
            text=True,
        )
        payload = json.loads(completed.stdout)
        self.assertEqual(payload["range"], {"min_count": 0, "max_count": 511})
        self.assertEqual(len(payload["candidates"]), 1)
        candidate = payload["candidates"][0]
        self.assertEqual(candidate["total_collisions"], 32)
        self.assertEqual(candidate["breakdown"]["mercury"], 32)
        self.assertEqual(candidate["self_collision_groups"], [])


if __name__ == "__main__":
    unittest.main()
