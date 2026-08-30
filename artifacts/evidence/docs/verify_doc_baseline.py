#!/usr/bin/env python3
"""Lean read-only validator for the Project Hotfix documentation baseline."""

from collections import Counter, defaultdict, deque
from decimal import Decimal
from hashlib import sha256
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[3]
ACTIVE_DOCS = [
    "docs/00_DOCUMENT_INDEX.md", "docs/01_PRD.md", "docs/02_SRS.md",
    "docs/PATCH_DESIGN.md",
    "docs/CHARACTER_TECHNICAL_SPEC.md", "docs/ART_DIRECTION.md",
    "docs/UI_UX_FLOW.md", "docs/MAP_DESIGN_GUIDE.md",
    "docs/MAP_P00_CONSTRUCTION_DROP.md", "docs/WEAPON_DESIGN.md",
    "docs/03_IMPLEMENTATION_PLAN.md", "docs/04_IMPLEMENTATION_TRACEABILITY.md",
]
VERSION_MARKERS = {
    "docs/01_PRD.md": "1.8.0", "docs/02_SRS.md": "1.8.0",
    "docs/PATCH_DESIGN.md": "0.5.0",
    "docs/CHARACTER_TECHNICAL_SPEC.md": "0.11.0",
    "docs/ART_DIRECTION.md": "1.8.0", "docs/UI_UX_FLOW.md": "1.8.0",
    "docs/MAP_DESIGN_GUIDE.md": "1.8.0",
    "docs/MAP_P00_CONSTRUCTION_DROP.md": "0.7.0",
    "docs/WEAPON_DESIGN.md": "0.7.0",
    "docs/03_IMPLEMENTATION_PLAN.md": "2.5",
    "docs/04_IMPLEMENTATION_TRACEABILITY.md": "1.5",
}
EXPECTED_PRD_IDS = 31
EXPECTED_SRS_IDS = 186
EXPECTED_AT_IDS = 44
EXPECTED_POLICY_SRS_IDS = {
    "SRS-UI-005", "SRS-UI-006", "SRS-NET-013", "SRS-NET-014",
    "SRS-NET-015", "SRS-SYS-027", "SRS-APPEAR-023", "SRS-NFR-011",
}
EXPECTED_POLICY_AT_IDS = {"AT-041", "AT-042", "AT-043", "AT-044"}
EXPECTED_PATCH_IDS = 12
EXPECTED_PATCH_ID_SET = {f"PATCH-PROT-{number:03d}" for number in range(1, 13)}
EXPECTED_PATCH_ROWS = {
    "PATCH-PROT-001": ("TRG-JUMP-ACCEPTED", "EFF-JUMP-HIGHER", "점프하면 더 높이 뜹니다."),
    "PATCH-PROT-002": ("TRG-JUMP-ACCEPTED", "EFF-JUMP-PULSE", "점프하면 주변의 다른 플레이어를 밀어냅니다."),
    "PATCH-PROT-003": ("TRG-ATTACK-HIT-CONFIRMED", "EFF-HIT-KNOCKBACK", "공격을 맞히면 맞은 플레이어가 더 멀리 밀려납니다."),
    "PATCH-PROT-004": ("TRG-ATTACK-HIT-CONFIRMED", "EFF-ATTACKER-RECOIL", "공격을 맞히면 공격한 플레이어도 뒤로 밀려납니다."),
    "PATCH-PROT-005": ("TRG-PLAYER-GRAB-ESTABLISHED", "EFF-THROW-RESISTANCE-LOW", "플레이어를 잡으면 잡힌 플레이어를 잠시 더 쉽게 들어 던질 수 있습니다."),
    "PATCH-PROT-006": ("TRG-PLAYER-GRAB-ESTABLISHED", "EFF-GRIP-STRONGER", "플레이어를 잡으면 현재 잡기가 잠시 더 강해집니다."),
    "PATCH-PROT-007": ("TRG-DOWN-EPISODE-START", "EFF-RAGDOLL-SLIDE", "다운되면 바닥에서 더 멀리 미끄러집니다."),
    "PATCH-PROT-008": ("TRG-DOWN-EPISODE-START", "EFF-RAGDOLL-BOUNCE", "다운되면 몸이 한 번 튀어 오릅니다."),
    "PATCH-PROT-009": ("TRG-WEAPON-SUPPLY-SCHEDULED", "EFF-WEAPON-SUPPLY-DOUBLE", "보급 시간이 되면 무기 두 개가 동시에 떨어집니다."),
    "PATCH-PROT-010": ("TRG-WEAPON-SUPPLY-SCHEDULED", "EFF-WEAPON-SUPPLY-SECOND-WAVE", "보급 시간이 되면 잠시 뒤 무기가 한 번 더 떨어집니다."),
    "PATCH-PROT-011": ("TRG-WEAPON-HIT-CONFIRMED", "EFF-VICTIM-HELD-WEAPON-FORCED-DROP", "무기로 공격을 맞히면 맞은 플레이어가 들고 있던 무기를 놓칩니다."),
    "PATCH-PROT-012": ("TRG-WEAPON-HIT-CONFIRMED", "EFF-ATTACKER-SOURCE-WEAPON-FORCED-DROP", "무기로 공격을 맞히면 공격한 플레이어도 사용한 무기를 놓칩니다."),
}
EXPECTED_PLAN_TASKS = 178
EXPECTED_PLAN_EFFORT = Decimal("258.0")
EXPECTED_POLICY_TASK_IDS = {
    "FDN-010", "FDN-011", "ART-001", "LIC-001", "BLD-001",
    "INP-006", "NET-015", "UI-007", "APT-007",
}
ALLOWED_EFFORT = {Decimal("0.5"), Decimal("1"), Decimal("1.5"), Decimal("2")}


def cells(line):
    return [part.strip() for part in line.strip().strip("|").split("|")]


def expand_task_refs(value):
    result = []
    pattern = re.compile(r"([A-Z][A-Z0-9]*)-(\d{3})(?:\.\.(\d{3}))?")
    for match in pattern.finditer(value):
        start = int(match.group(2))
        end = int(match.group(3)) if match.group(3) else start
        result.extend(f"{match.group(1)}-{number:03d}" for number in range(start, end + 1))
    return result


def expand_srs_refs(text):
    result = set()
    pattern = re.compile(r"(SRS-[A-Z]+)-(\d{3})(?:\.\.(\d{3}))?")
    for match in pattern.finditer(text):
        start = int(match.group(2))
        end = int(match.group(3)) if match.group(3) else start
        result.update(f"{match.group(1)}-{number:03d}" for number in range(start, end + 1))
    return result


def expand_prd_refs(text):
    result = set()
    pattern = re.compile(r"(PRD-[A-Z]+)-(\d{3})(?:\.\.(\d{3}))?")
    for match in pattern.finditer(text):
        start = int(match.group(2))
        end = int(match.group(3)) if match.group(3) else start
        result.update(f"{match.group(1)}-{number:03d}" for number in range(start, end + 1))
    return result


def markdown_problems(relative):
    path = ROOT / relative
    text = path.read_text()
    fence = chr(96) * 3
    odd_fence = sum(line.startswith(fence) for line in text.splitlines()) % 2
    conflicts = [
        number for number, line in enumerate(text.splitlines(), 1)
        if re.match(r"^(<<<<<<<|=======|>>>>>>>)", line)
    ]
    missing_links = []
    for number, line in enumerate(text.splitlines(), 1):
        for target in re.findall(r"\[[^\]]*\]\(([^)]+)\)", line):
            target = target.strip("<>")
            if target.startswith(("http://", "https://", "#", "mailto:")):
                continue
            path_part = target.split("#", 1)[0]
            if path_part and not (path.parent / path_part).resolve().exists():
                missing_links.append((number, target))
    return odd_fence, conflicts, missing_links


def main():
    failures = []
    print("EV-DOC-RAW-VALIDATION 2026-08-26 r06")
    print("COMMAND_KIND=read-only documentation parser; Build=0 Docker=0 Deploy=0")
    missing_docs = [relative for relative in ACTIVE_DOCS if not (ROOT / relative).exists()]
    print(f"ACTIVE_DOCS files={len(ACTIVE_DOCS) - len(missing_docs)}/{len(ACTIVE_DOCS)}")
    if missing_docs:
        print(f"MISSING_DOCS={missing_docs}")
        print("FINAL_RESULT=FAIL")
        return 1

    for relative in ACTIVE_DOCS:
        path = ROOT / relative
        print(
            f"{relative}|sha256={sha256(path.read_bytes()).hexdigest()}|"
            f"lines={len(path.read_text().splitlines())}"
        )

    version_mismatch = [
        (relative, marker) for relative, marker in VERSION_MARKERS.items()
        if marker not in (ROOT / relative).read_text()
    ]
    print(f"VERSIONS mismatch={len(version_mismatch)}")
    if version_mismatch:
        failures.append("Document versions")

    prd_text = (ROOT / "docs/01_PRD.md").read_text()
    prd_rows = re.findall(r"^\| `?(PRD-[A-Z]+-\d{3})`? \|", prd_text, re.MULTILINE)
    print(f"PRD_IDS rows={len(prd_rows)} unique={len(set(prd_rows))}")
    if len(prd_rows) != EXPECTED_PRD_IDS or len(set(prd_rows)) != EXPECTED_PRD_IDS:
        failures.append("PRD ID set")

    srs_text = (ROOT / "docs/02_SRS.md").read_text()
    srs_rows = re.findall(r"^\| (SRS-[A-Z]+-\d{3}) \|", srs_text, re.MULTILINE)
    at_rows = re.findall(r"^\| (AT-\d{3}) \|", srs_text, re.MULTILINE)
    print(f"SRS_IDS rows={len(srs_rows)} unique={len(set(srs_rows))}")
    print(f"AT_IDS rows={len(at_rows)} unique={len(set(at_rows))}")
    if len(srs_rows) != EXPECTED_SRS_IDS or len(set(srs_rows)) != EXPECTED_SRS_IDS:
        failures.append("SRS ID set")
    if len(at_rows) != EXPECTED_AT_IDS or len(set(at_rows)) != EXPECTED_AT_IDS:
        failures.append("AT ID set")
    missing_policy_srs = sorted(EXPECTED_POLICY_SRS_IDS - set(srs_rows))
    missing_policy_at = sorted(EXPECTED_POLICY_AT_IDS - set(at_rows))
    print(
        f"POLICY_IDS srs_missing={len(missing_policy_srs)} "
        f"at_missing={len(missing_policy_at)}"
    )
    if missing_policy_srs or missing_policy_at:
        print(f"POLICY_ID_DETAIL srs={missing_policy_srs} at={missing_policy_at}")
        failures.append("Approved policy IDs")

    patch_text = (ROOT / "docs/PATCH_DESIGN.md").read_text()
    patch_rows = re.findall(r"^\| \x60?(PATCH-PROT-\d{3})\x60? \|", patch_text, re.MULTILINE)
    print(f"PATCH_IDS rows={len(patch_rows)} unique={len(set(patch_rows))}")
    patch_missing = sorted(EXPECTED_PATCH_ID_SET - set(patch_rows))
    patch_extra = sorted(set(patch_rows) - EXPECTED_PATCH_ID_SET)
    print(f"PATCH_EXACT_IDS missing={len(patch_missing)} extra={len(patch_extra)}")
    if (
        len(patch_rows) != EXPECTED_PATCH_IDS
        or len(set(patch_rows)) != EXPECTED_PATCH_IDS
        or patch_missing or patch_extra
    ):
        failures.append("Patch ID set")
    parsed_patch_rows = {
        patch_id: (trigger_id, effect_id, alpha_text)
        for patch_id, trigger_id, effect_id, alpha_text in re.findall(
            r"^\| `(PATCH-PROT-\d{3})` \| `(TRG-[^`]+)` \| `(EFF-[^`]+)` \| `([^`]+)` \|$",
            patch_text,
            re.MULTILINE,
        )
    }
    patch_binding_mismatch = sorted(
        patch_id for patch_id, expected in EXPECTED_PATCH_ROWS.items()
        if parsed_patch_rows.get(patch_id) != expected
    )
    print(f"PATCH_BINDINGS mismatch={len(patch_binding_mismatch)}")
    if patch_binding_mismatch:
        print(f"PATCH_BINDING_DETAIL={patch_binding_mismatch}")
        failures.append("Patch bindings")

    plan_text = (ROOT / "docs/03_IMPLEMENTATION_PLAN.md").read_text()
    task_pattern = re.compile(r"^\| ([A-Z][A-Z0-9]*-\d{3}) \| (0\.5|1|1\.5|2) \|")
    tasks, task_rows, column_errors = {}, [], []
    for number, line in enumerate(plan_text.splitlines(), 1):
        match = task_pattern.match(line)
        if not match:
            continue
        task_id = match.group(1)
        row = cells(line)
        task_rows.append(task_id)
        tasks[task_id] = {"line": number, "effort": Decimal(match.group(2)), "cells": row}
        if len(row) != 9:
            column_errors.append((number, task_id, len(row)))
    duplicate_tasks = [task for task, count in Counter(task_rows).items() if count > 1]
    effort = sum(value["effort"] for value in tasks.values())
    print(
        f"PLAN_TASKS rows={len(task_rows)} unique={len(tasks)} effort_days={effort} "
        f"duplicate={len(duplicate_tasks)} column_errors={len(column_errors)}"
    )
    if (
        duplicate_tasks
        or column_errors
        or len(tasks) != EXPECTED_PLAN_TASKS
        or effort != EXPECTED_PLAN_EFFORT
    ):
        failures.append("Plan task schema")
    missing_policy_tasks = sorted(EXPECTED_POLICY_TASK_IDS - set(tasks))
    print(f"POLICY_TASKS missing={len(missing_policy_tasks)}")
    if missing_policy_tasks:
        print(f"POLICY_TASK_DETAIL={missing_policy_tasks}")
        failures.append("Approved policy tasks")
    if any(value["effort"] not in ALLOWED_EFFORT for value in tasks.values()):
        failures.append("Plan effort")

    missing_dependencies = []
    edges = defaultdict(set)
    indegree = {task_id: 0 for task_id in tasks}
    for task_id, value in tasks.items():
        for dependency in expand_task_refs(value["cells"][3]):
            if dependency not in tasks:
                missing_dependencies.append((task_id, dependency))
            elif task_id not in edges[dependency]:
                edges[dependency].add(task_id)
                indegree[task_id] += 1
    ready = deque(sorted(task for task, degree in indegree.items() if degree == 0))
    visited = []
    while ready:
        task = ready.popleft()
        visited.append(task)
        for child in sorted(edges[task]):
            indegree[child] -= 1
            if indegree[child] == 0:
                ready.append(child)
    cycle_nodes = len(tasks) - len(visited)
    print(f"PLAN_DEPS missing={len(missing_dependencies)} cycle_nodes={cycle_nodes}")
    if missing_dependencies or cycle_nodes:
        print(f"PLAN_DEP_DETAIL={missing_dependencies[:20]}")
        failures.append("Plan dependencies")

    trace_text = (ROOT / "docs/04_IMPLEMENTATION_TRACEABILITY.md").read_text()
    trace_prd = expand_prd_refs(trace_text)
    trace_srs = expand_srs_refs(trace_text)
    trace_at_rows = re.findall(r"(?<![A-Z])AT-\d{3}", trace_text)
    trace_at = set(trace_at_rows)
    missing_trace_prd = sorted(set(prd_rows) - trace_prd)
    extra_trace_prd = sorted(trace_prd - set(prd_rows))
    missing_trace_srs = sorted(set(srs_rows) - trace_srs)
    extra_trace_srs = sorted(trace_srs - set(srs_rows))
    missing_trace_at = sorted(set(at_rows) - trace_at)
    duplicate_trace_at = len(trace_at_rows) - len(trace_at)
    trace_without_requirements = re.sub(
        r"SRS-[A-Z]+-\d{3}(?:\.\.\d{3})?", "", trace_text
    )
    trace_without_requirements = re.sub(
        r"PRD-[A-Z]+-\d{3}(?:\.\.\d{3})?", "", trace_without_requirements
    )
    trace_without_requirements = re.sub(r"(?<![A-Z])AT-\d{3}", "", trace_without_requirements)
    trace_task_refs = set(expand_task_refs(trace_without_requirements))
    missing_trace_tasks = sorted(trace_task_refs - set(tasks))
    print(
        f"TRACE prd_missing={len(missing_trace_prd)} prd_extra={len(extra_trace_prd)} "
        f"srs_missing={len(missing_trace_srs)} srs_extra={len(extra_trace_srs)} "
        f"at_missing={len(missing_trace_at)} at_duplicate={duplicate_trace_at} "
        f"task_missing={len(missing_trace_tasks)}"
    )
    if (
        missing_trace_prd or extra_trace_prd
        or missing_trace_srs or extra_trace_srs
        or missing_trace_at or duplicate_trace_at
        or missing_trace_tasks
    ):
        print(f"TRACE_PRD_MISSING={missing_trace_prd[:20]}")
        print(f"TRACE_SRS_MISSING={missing_trace_srs[:20]}")
        print(f"TRACE_TASK_MISSING={missing_trace_tasks[:20]}")
        failures.append("Trace coverage")

    markdown_failures = []
    for relative in ACTIVE_DOCS:
        odd, conflicts, missing_links = markdown_problems(relative)
        if odd or conflicts or missing_links:
            markdown_failures.append((relative, odd, conflicts, missing_links[:10]))
    print(f"MARKDOWN problems={len(markdown_failures)}")
    if markdown_failures:
        print(f"MARKDOWN_DETAIL={markdown_failures[:10]}")
        failures.append("Markdown integrity")

    archive_root = ROOT / "docs/archive"
    archive_files = sorted(
        str(path.relative_to(ROOT)) for path in archive_root.rglob("*") if path.is_file()
    ) if archive_root.exists() else []
    print(f"UNUSED_ARCHIVE files={len(archive_files)}")
    if archive_files:
        print(f"UNUSED_ARCHIVE_DETAIL={archive_files}")
        failures.append("Unused archive")
    archive_mentions = [
        relative for relative in ACTIVE_DOCS
        if re.search(r"\barchive(?:d)?\b", (ROOT / relative).read_text(), re.IGNORECASE)
    ]
    print(f"ARCHIVE_REFERENCES files={len(archive_mentions)}")
    if archive_mentions:
        print(f"ARCHIVE_REFERENCE_DETAIL={archive_mentions}")
        failures.append("Archive references")

    container_files = []
    for path in ROOT.rglob("*"):
        if not path.is_file():
            continue
        name = path.name.lower()
        if name == "dockerfile" or name.startswith("dockerfile.") or name in {
            "docker-compose.yml", "docker-compose.yaml", "compose.yml", "compose.yaml"
        }:
            container_files.append(str(path.relative_to(ROOT)))
    print(f"FORBIDDEN_CONTAINER_ARTIFACTS files={len(container_files)}")
    if container_files:
        failures.append("Container artifacts")

    all_text = "\n".join((ROOT / relative).read_text() for relative in ACTIVE_DOCS)
    forbidden_legacy_tokens = [
        "DevelopmentIdentityProvider", "PartyJoinGrant", "TransportLocator",
        "GameCoordinator", "AppearanceBakeWorker", "optional container",
        "배포 container를 채택",
    ]
    legacy_hits = [token for token in forbidden_legacy_tokens if token in all_text]
    forbidden_task_prefixes = {
        task for task in tasks if task.startswith(("BKE-", "DAT-", "OBS-"))
    }
    print(
        f"LEGACY_ARCHITECTURE token_hits={len(legacy_hits)} "
        f"forbidden_task_prefixes={len(forbidden_task_prefixes)}"
    )
    if legacy_hits or forbidden_task_prefixes:
        print(f"LEGACY_DETAIL tokens={legacy_hits} tasks={sorted(forbidden_task_prefixes)}")
        failures.append("Legacy architecture")

    required_patterns = {
        "host_start": r"Host.*Start|방장.*Start",
        "no_host_ready": r"Host Ready|방장.*Ready.*없",
        "sprint": r"Sprint",
        "cursor_toggle": r"Esc.*(다시|toggle|닫)",
        "tab_modes": r"Hold.*Toggle",
        "down_base": r"첫 Down.*Base|첫.*다운.*Base",
        "local_preset": r"local.*Preset|로컬.*Preset",
        "steam_code": r"SteamLobbyId",
        "all_player_counts": r"2.*3.*4인",
        "weapon_alpha": r"무기.*Alpha|Alpha.*무기",
        "ready_color": r"ReadyTeal",
        "patch_text_ui": r"평문.*Trigger|plain.?text.*Trigger",
        "patch_projected_set": r"projected active set",
        "patch12_design_gate": r"UG-PATCH12-DESIGN",
        "weapon_supply_profile_2p": r"2인.*10.*22.*(?:cap|상한).*2",
        "weapon_supply_profile_3p": r"3인.*8.*16.*(?:cap|상한).*2",
        "weapon_supply_profile_4p": r"4인.*6.*12.*(?:cap|상한).*3",
        "weapon_supply_cap_states": r"Incoming.*Loose.*Held.*Spent",
        "weapon_supply_capacity": r"CapacityLimited",
        "weapon_supply_frozen_profile": r"frozen Supply profile|Round.*profile.*고정|profile을.*고정",
        "weapon_supply_catalog_version": r"WeaponCatalogVersion",
        "weapon_landing_clearance": r"LandingClearance",
        "weapon_no_safe_zone": r"NoSafeDropZone",
        "weapon_landing_blocked": r"LandingBlocked",
        "weapon_cleanup_boundary": r"WeaponCleanupBoundary",
        "air_left_right_kick": r"AirKick|Air Kick",
        "air_dropkick": r"Dropkick",
        "air_chord_window": r"60/80/100ms|DualClickChord|dual-click chord",
        "air_kick_anchor": r"KickAnchor",
        "dropkick_non_down": r"DropkickRecovery.*DownCount|DropkickRecovery.*non-Down",
        "action_root_motion_zero": r"Root Motion.*0|root motion.*0",
        "weapon_m1911_archetype": r"M1911-inspired",
        "weapon_ak47_archetype": r"AK-47-inspired",
        "weapon_sledgehammer_archetype": r"sledgehammer",
        "weapon_art_gate": r"UG-WEAPON-ART",
        "pistol_total_ammo_7": r"Pistol.*(?:7발|total7|Ammo.*7)",
        "ak_total_ammo_30": r"AK.*(?:30발|total30|Ammo.*30)",
        "firearm_no_reload": r"no-reload|Reload.*0|재장전.*없",
        "firearm_spent_cleanup": r"SpentPendingCleanup",
        "host_projectile_sweep": r"swept SphereCast|SphereCast",
        "firearm_shot_sequence": r"ShotSequence",
        "firearm_recoil_accumulator": r"RecoilAccumulator",
        "firearm_spread_bloom": r"SpreadBloom",
        "firearm_combat_phase": r"Playing.*SuddenDeath.*RoundResult",
        "firearm_delayed_patch12": r"delayed.*Spent.*NoEligibleTarget|지연.*Spent.*NoEligibleTarget",
        "weapon_patch_trigger": r"TRG-WEAPON-HIT-CONFIRMED",
        "weapon_patch_last_id": r"PATCH-PROT-012",
        "unity_63_lts": r"Unity ?6\.3 LTS",
        "blender_52_lts": r"Blender ?5\.2 LTS",
        "persistent_match_hud_zero": r"Persistent Match HUD.*0|Persistent HUD.*0",
        "match_local_only_nonpause": r"Match Esc.*local-only|Simulation.*멈추지.*local-only",
        "explicit_guest_leave_forfeit": r"Explicit Guest Leave.*즉시 Forfeit|명시.*Guest Leave.*즉시.*Forfeit",
        "disconnect_grace_vulnerable": r"30초.*(?:physical|Character).*(?:vulnerable|취약)|30초.*(?:vulnerable|취약).*(?:Character|물리)",
        "forfeit_not_patch_author": r"Forfeit.*PatchAuthor.*(?:아니|0)",
        "opponent_left_no_auto_win": r"OpponentLeft.*Lobby|상대가 나갔습니다.*Lobby",
        "host_loss_session_end": r"Host (?:Leave/)?Loss.*Session 종료|Host.*loss.*Session.*종료",
        "alpha_korean_only": r"Korean-only",
        "alpha_bgm_zero": r"BGM ?0",
        "alpha_placeholder_cosmetic": r"placeholder.*(?:EyeSet|Cosmetic)|(?:EyeSet|Mustache|Headwear).*placeholder",
        "alpha_placeholder_categories": r"EyeSet.*Mustache.*Headwear",
        "alpha_audio_controls_zero": r"Alpha.*(?:사용자-facing )?audio channel control.*0|사용자.*audio.*control.*BGM.*0",
        "post_alpha_key_help": r"(?:MainMenu )?key help.*post-Alpha|post-Alpha.*(?:MainMenu )?key help",
        "ug_doc_passed": r"UG-DOC.*PASSED|PASSED.*UG-DOC",
        "alpha_gate_g2_g3_qa": r"ALP-001.*QA-003\.\.010",
        "reconnect_full_state": r"Score.*Participation.*Transform.*Down.*Hazard.*Scene.*Alive/Spectator",
        "post_alpha_accessibility_settings": r"post-Alpha.*(?:Key Rebinding|key rebind|키 재지정)|(?:Key Rebinding|key rebind|키 재지정).*post-Alpha",
        "w1_editor_no_player_build": r"WPN-003.*Editor comparison.*Player Build 0",
    }
    contract_missing = [
        name for name, pattern in required_patterns.items()
        if not re.search(pattern, all_text, re.IGNORECASE)
    ]
    print(f"CONTRACT_MARKERS missing={len(contract_missing)}")
    if contract_missing:
        print(f"CONTRACT_MARKER_DETAIL={contract_missing}")
        failures.append("Contract markers")
    forbidden_product_names = [
        token for token in ("핫패치", "Hotpatch")
        if token in all_text
    ]
    print(f"PATCH_PRODUCT_NAME forbidden_hits={len(forbidden_product_names)}")
    if forbidden_product_names:
        failures.append("Patch product name")

    stale_patch_scope_tokens = [
        token for token in ("UG-PATCH8-DESIGN", "UG-PATCH8", "Patch 09~20", "Patch09..20")
        if token in all_text
    ]
    print(f"STALE_PATCH_SCOPE token_hits={len(stale_patch_scope_tokens)}")
    if stale_patch_scope_tokens:
        print(f"STALE_PATCH_SCOPE_DETAIL={stale_patch_scope_tokens}")
        failures.append("Stale Patch scope")

    stale_active_reference_tokens = [
        token for token in (
            "PRD 1.5.0", "SRS 1.5.0", "PATCH_DESIGN 0.2.0",
            "`PATCH_DESIGN.md` 0.2.0", "Implementation Plan 2.2",
            "Plan 2.2", "Trace 1.2", "PRD 1.6.0", "SRS 1.6.0",
            "PATCH_DESIGN 0.3.0", "`PATCH_DESIGN.md` 0.3.0",
            "Implementation Plan 2.3", "Plan 2.3", "Trace 1.3", "AirChordWindow",
            "AirKickLeft", "AirKickRight", "PRD 1.7.0", "SRS 1.7.0",
            "PATCH_DESIGN 0.4.0", "`PATCH_DESIGN.md` 0.4.0",
            "Implementation Plan 2.4", "Plan 2.4", "Trace 1.4",
            "Baseline Candidate", "provisional WBS", "UG-DOC`는 별도 USER_REVIEW",
            "Listen Server", "Client와 Server를 함께 실행",
        )
        if token in all_text
    ]
    print(f"STALE_ACTIVE_REFS token_hits={len(stale_active_reference_tokens)}")
    if stale_active_reference_tokens:
        print(f"STALE_ACTIVE_REFS_DETAIL={stale_active_reference_tokens}")
        failures.append("Stale active references")

    artifact = ROOT / "artifacts/review/character/C1_CHARACTER_HYBRID_CORE_v0.13_BELLY_CORRECTED_REVIEW.png"
    artifact_hash = sha256(artifact.read_bytes()).hexdigest() if artifact.exists() else "MISSING"
    print(f"C1A_ARTIFACT exists={artifact.exists()} sha256={artifact_hash}")
    if artifact_hash != "c1def169cefd59f19339a5b5edbac2dfd0c8fe9a05eba9ee0afb1ae598bab616":
        failures.append("C1a artifact")

    print("FINAL_RESULT=" + ("PASS" if not failures else "FAIL"))
    if failures:
        print("FAILURES=" + ",".join(failures))
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
