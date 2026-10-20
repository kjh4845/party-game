"""Regressions for stale approval and obsolete dependency claims in the WBS."""

import copy
import importlib.util
import json
from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "doc_baseline", ROOT / "artifacts/evidence/docs/verify_doc_baseline.py"
)
VALIDATOR = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VALIDATOR)


class DocumentWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.texts = {p: (ROOT / p).read_text() for p in VALIDATOR.ACTIVE_DOCS}
        self.rows = {}
        for line in self.texts["docs/03_IMPLEMENTATION_PLAN.md"].splitlines():
            row = VALIDATOR.cells(line)
            if len(row) == 9 and re.fullmatch(r"[A-Z][A-Z0-9]*-\d{3}", row[0]):
                self.rows[row[0]] = row
        self.approval = json.loads(
            (ROOT / "BlenderSource/Characters/C1B-RW-016-preview/NeutralApprovalRecord.json").read_text()
        )
        self.rig = json.loads(
            (ROOT / "BlenderSource/Characters/C1B-RW-020-helper-rig-preview/RigQAReport.json").read_text()
        )

    def problems(self):
        return VALIDATOR.character_workflow_problems(
            self.texts, self.rows, self.approval, self.rig
        )

    def test_current_workflow_matches_recorded_approval(self):
        self.assertEqual([], self.problems())

    def test_stale_current_candidate_is_rejected_even_with_other_r16_mentions(self):
        self.texts["docs/00_DOCUMENT_INDEX.md"] += "\n현재 r11 승인 대기\n"
        self.assertIn("STALE_CURRENT_CHARACTER:00_DOCUMENT_INDEX", self.problems())

    def test_approved_neutral_cannot_silently_return_to_user_review(self):
        self.rows["C1BRW-003"][-1] = "USER_REVIEW"
        self.assertIn("NEUTRAL_APPROVAL_NOT_PROPAGATED", self.problems())

    def test_valid_task_id_can_still_be_an_obsolete_dependency(self):
        self.rows["ANP-001"][3] = "C1B-004,FDN-003"
        self.assertIn("ACTIVE_DEPENDS_ON_SUPERSEDED:ANP-001", self.problems())

    def test_network_completion_is_not_an_offline_anp_gate(self):
        self.rows["ANP-003"][2] = "Ragdoll/network 통합"
        self.assertIn("ANP_OFFLINE_SCOPE", self.problems())

    def test_static_stage_must_not_claim_animation_completion(self):
        path = "docs/03_IMPLEMENTATION_PLAN.md"
        self.texts[path] = self.texts[path].replace(
            "## 12.", "Pose·Animation·Unity parity\n\n## 12.", 1
        )
        self.assertIn("STATIC_STAGE_CLAIMS_ANIMATION", self.problems())

    def test_a_new_rig_record_requires_current_documents_to_follow(self):
        self.rig = copy.deepcopy(self.rig)
        self.rig["revision"] = "r99"
        self.assertIn("CURRENT_CHARACTER_MISSING:00_DOCUMENT_INDEX", self.problems())


if __name__ == "__main__":
    unittest.main()
