#!/usr/bin/env ruby

require "fileutils"
require "json"
require "minitest/autorun"
require "open3"
require "pathname"
require "rbconfig"
require "set"
require "tmpdir"
require "yaml"

class VerifyForbiddenInfrastructureTest < Minitest::Test
  REPOSITORY_ROOT = Pathname.new(__dir__).join("../..").expand_path
  VERIFIER = REPOSITORY_ROOT.join("tools/verify_forbidden_infrastructure.rb")
  POLICY = REPOSITORY_ROOT.join("config/infrastructure/ForbiddenInfrastructurePolicy.yaml")
  MAX_SCAN_BYTES = 1_048_576
  DEFAULT_PACKAGE_DOCUMENT = {
    "dependencies" => {
      "com.unity.collab-proxy" => "2.11.3",
      "com.unity.multiplayer.center" => "1.0.1",
      "com.unity.services.core" => "1.0.0",
      "com.unity.transport" => "2.6.0",
      "com.unity.modules.unitywebrequest" => "1.0.0",
    },
  }.freeze
  EXERCISED_RULE_IDS = Set.new(%w[
    CONTAINER_ENTRY_FILE
    EXTERNAL_SERVICE_DIRECTORY
    DEDICATED_DIRECTORY
    CONTAINER_DIRECTORY
    DATABASE_FILE
    INFRASTRUCTURE_AS_CODE_FILE
    OCI_IMAGE_FILE
    CONTAINER_VARIANT_FILE
    CONTAINER_ORCHESTRATION_FILE
    DEDICATED_BUILD_ARTIFACT
    BACKEND_DEPLOY_DIRECTORY
    STANDALONE_GAME_SERVER_PROJECT
    STANDALONE_SERVER_PROJECT
    BACKEND_SDK_ARTIFACT
    DATABASE_SDK_ARTIFACT
    DEDICATED_NAMED_ARTIFACT
    CONTAINER_COMMAND
    WORKFLOW_CONTAINER_JOB
    KUBERNETES_MANIFEST
    KUBERNETES_OR_TERRAFORM_COMMAND
    DEDICATED_BUILD_SIGNATURE
    DEDICATED_MULTIPLAYER_ROLE
    DEDICATED_BUILDPROFILE_SUBTARGET
    DEDICATED_BUILDPROFILE_ASSET
    DEDICATED_PROCESS_ENTRYPOINT
    BACKEND_SERVER_FRAMEWORK
    REMOTE_BACKEND_CLIENT_API
    BACKEND_SCRIPT_FRAMEWORK
    REMOTE_BLOB_SDK
    DATABASE_RUNTIME_API
    DATABASE_SCRIPT_API
    UNITY_CLOUD_BACKEND_API
    STEAM_DEDICATED_SERVER_API
    UNITY_HOSTED_BACKEND_PACKAGE
    THIRD_PARTY_BACKEND_PACKAGE
    DATABASE_PACKAGE
    DEDICATED_SERVER_PACKAGE
  ]).freeze

  def test_current_repository_passes
    stdout, stderr, status = run_verifier(REPOSITORY_ROOT)

    assert_equal 0, status, stderr + stdout
    assert_includes stdout, "PACKAGE_MANIFESTS_SCANNED=2"
    assert_includes stdout, "TOTAL_VIOLATIONS=0"
    assert_includes stdout, "FINAL_RESULT=PASS"
  end

  def test_narrative_docs_and_evidence_are_allowed_but_artifact_names_are_not
    with_repository(
      "docs/backend/dedicated-server-design.md" =>
        "Docker build, PlayFab, Database and Dedicated Server must remain zero.\n",
      "artifacts/evidence/G0/EV-SCOPE.yaml" =>
        "commandsOrManualSteps: ['docker build fixture is rejected']\n",
      "artifacts/evidence/G0/EV-SCOPE.txt" => "UNITY_SERVER=0 CREATE TABLE=0\n"
    ) do |root|
      stdout, stderr, status = run_verifier(root)
      assert_equal 0, status, stderr + stdout
      assert_includes stdout, "FINAL_RESULT=PASS"
    end

    with_repository("docs/Dockerfile" => "narrative bypass must fail\n") do |root|
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=CONTAINER_ENTRY_FILE"
      assert_includes stdout, "path=docs/Dockerfile"
    end
  end

  def test_host_p2p_known_unity_defaults_and_generic_yaml_keys_are_allowed
    with_repository(
      "Project hotfix/Assets/ProjectHotfix/Runtime/Transport/AuthorityHost.cs" => <<~CS,
        using Unity.Networking.Transport;
        internal sealed class AuthorityHost
        {
            private void BindAndListen() { /* Steam Lobby and P2P/SDR relay are adapters. */ }
        }
      CS
      "Project hotfix/Assets/ProjectHotfix/Runtime/GameServer/AuthorityHost.cs" => <<~CS,
        using Unity.Networking.Transport;
        internal sealed class AuthorityHost { private void BindAndListen() {} }
      CS
      "Project hotfix/Assets/ProjectHotfix/Tests/TransportBoundaryTests.cs" =>
        "var rejected = new[] { \"com.unity.services.relay\" };\n",
      "Project hotfix/ProjectSettings/ProjectSettings.asset" => <<~YAML,
        dedicatedServerOptimizations: 1
        scriptingBackend: {}
        m_BakeBackend: 0
        m_EnableMultiplayerRoles: 0
        m_CacheServerMode: 0
      YAML
      "config/gameplay.yaml" => "services:\n  - score\ncontainer: map-prop\n"
    ) do |root|
      stdout, stderr, status = run_verifier(root)

      assert_equal 0, status, stderr + stdout
      assert_includes stdout, "BACKEND_VIOLATIONS=0"
      assert_includes stdout, "DEDICATED_VIOLATIONS=0"
      assert_includes stdout, "CONTAINER_VIOLATIONS=0"
      assert_includes stdout, "FINAL_RESULT=PASS"
    end
  end

  def test_every_artifact_rule_rejects_a_representative_path
    fixtures = {
      "CONTAINER_ENTRY_FILE" => { "deploy/.dockerignore" => "fixture\n" },
      "EXTERNAL_SERVICE_DIRECTORY" => { "ops/blob-store/worker.cs" => "fixture\n" },
      "DEDICATED_DIRECTORY" => { "release/dedicated-server/game.bin" => "fixture\n" },
      "CONTAINER_DIRECTORY" => { "deploy/k8s/config.txt" => "fixture\n" },
      "DATABASE_FILE" => { "data/schema.sql" => "fixture\n" },
      "INFRASTRUCTURE_AS_CODE_FILE" => { "deploy/main.tf" => "fixture\n" },
      "OCI_IMAGE_FILE" => { "release/game-image.oci" => "fixture\n" },
      "CONTAINER_VARIANT_FILE" => { "deploy/Containerfile.release" => "fixture\n" },
      "CONTAINER_ORCHESTRATION_FILE" => { "deploy/kustomization.yaml" => "fixture\n" },
      "DEDICATED_BUILD_ARTIFACT" => { "Builds/windows/server-build/game.bin" => "fixture\n" },
      "BACKEND_DEPLOY_DIRECTORY" => { "ops/backend/Program.cs" => "fixture\n" },
      "STANDALONE_GAME_SERVER_PROJECT" => { "runtime/game-server/Server.csproj" => "fixture\n" },
      "STANDALONE_SERVER_PROJECT" => { "server/Server.csproj" => "fixture\n" },
      "BACKEND_SDK_ARTIFACT" => { "Assets/Plugins/PlayFabSDK.dll" => "fixture\n" },
      "DATABASE_SDK_ARTIFACT" => { "Assets/Plugins/MySqlConnector.dll" => "fixture\n" },
      "DEDICATED_NAMED_ARTIFACT" => { "release/DedicatedServer.x86_64" => "fixture\n" },
    }
    extra_variants = {
      "deploy/Dockerfile.dev" => "CONTAINER_VARIANT_FILE",
      "deploy/compose.prod.yaml" => "CONTAINER_VARIANT_FILE",
      "deploy/Containerfile" => "CONTAINER_ENTRY_FILE",
      "image/oci-layout" => "CONTAINER_ENTRY_FILE",
    }

    fixtures.each do |expected_rule, files|
      with_repository(files) do |root|
        stdout, _stderr, status = run_verifier(root)
        assert_equal 1, status, expected_rule
        assert_includes stdout, "rule=#{expected_rule}", expected_rule
        assert_includes stdout, "FINAL_RESULT=FAIL", expected_rule
      end
    end
    extra_variants.each do |relative, expected_rule|
      with_repository(relative => "fixture\n") do |root|
        stdout, _stderr, status = run_verifier(root)
        assert_equal 1, status, relative
        assert_includes stdout, "rule=#{expected_rule}", relative
        assert_includes stdout, "path=#{relative}", relative
      end
    end
  end

  def test_every_content_rule_rejects_a_representative_signature_repository_wide
    secret = "super-secret-token-fdn009"
    files = {
      "release.sh" => "docker build -t image . # #{secret}\n",
      "release" => "docker login registry.invalid\n",
      "ops/buildx.sh" => "docker buildx build .\n",
      "ops/compose.sh" => "docker-compose up\n",
      "tools/tests/integration.sh" => "podman run forbidden\n",
      ".github/workflows/deploy.yml" => "jobs:\n  deploy:\n    container: forbidden/image\n",
      "ops/k8s.yml" => "apiVersion: apps/v1\nkind: Deployment\n",
      "ops/pod.yaml" => "apiVersion: v1\nkind: Pod\n",
      "ops/provision.sh" => "kubectl create namespace forbidden\n",
      "Project hotfix/Assets/ProjectHotfix/Tests/DedicatedMode.cs" => "#if UNITY_SERVER\n#endif\n",
      "Project hotfix/Assets/Settings/Server.buildprofile" => "m_Subtarget: 1\n",
      "Project hotfix/Assets/Settings/SteamServer.asset" => "BuildProfile:\n  m_Subtarget: 1\n",
      "Project hotfix/ProjectSettings/MultiplayerManager.asset" => "m_EnableMultiplayerRoles: 1\n",
      "Project hotfix/Assets/Editor/RemoteApi.cs" => "using Microsoft.AspNetCore.Hosting;\n",
      "Project hotfix/Assets/ProjectHotfix/Runtime/RemoteProfile.cs" => "using System.Net.Http; internal sealed class RemoteProfile { object Make() => new HttpClient(); }\n",
      "Project hotfix/Assets/ProjectHotfix/Runtime/RemoteTexture.cs" => "var request = UnityWebRequest.Get(\"https://party-backend.invalid/profile\");\n",
      "ops/app.py" => "from fastapi import FastAPI\n",
      "Project hotfix/Assets/Editor/Blob.cs" => "using Azure.Storage.Blobs;\n",
      "Project hotfix/Assets/Editor/Persistence.cs" => "using Microsoft.Data.Sqlite;\n",
      "ops/data.py" => "import sqlalchemy\n",
      "Project hotfix/Assets/Editor/Cloud.cs" => "using Unity.Services.Lobby;\n",
      "Project hotfix/Assets/ProjectHotfix/Runtime/SteamDedicated.cs" => "SteamGameServer.Init(0, 0, 0, 0, 0, \"x\");\n",
      "server/Program.cs" => "static void Main() { var listener = new TcpListener(default, 1); listener.Start(); }\n",
    }
    expected_rules = %w[
      CONTAINER_COMMAND
      WORKFLOW_CONTAINER_JOB
      KUBERNETES_MANIFEST
      KUBERNETES_OR_TERRAFORM_COMMAND
      DEDICATED_BUILD_SIGNATURE
      DEDICATED_MULTIPLAYER_ROLE
      BACKEND_SERVER_FRAMEWORK
      BACKEND_SCRIPT_FRAMEWORK
      REMOTE_BLOB_SDK
      DATABASE_RUNTIME_API
      DATABASE_SCRIPT_API
      UNITY_CLOUD_BACKEND_API
      DEDICATED_PROCESS_ENTRYPOINT
      DEDICATED_BUILDPROFILE_ASSET
      REMOTE_BACKEND_CLIENT_API
      STEAM_DEDICATED_SERVER_API
    ]

    with_repository(files, executables: ["release"]) do |root|
      stdout, stderr, status = run_verifier(root)

      assert_equal 1, status
      expected_rules.each { |rule| assert_includes stdout, "rule=#{rule}", rule }
      assert_includes stdout, "path=release.sh"
      assert_includes stdout, "path=release"
      assert_includes stdout, "path=ops/buildx.sh"
      assert_includes stdout, "path=ops/compose.sh"
      assert_includes stdout, "path=ops/pod.yaml"
      assert_includes stdout, "path=Project hotfix/Assets/Settings/Server.buildprofile"
      assert_includes stdout, "path=Project hotfix/Assets/Settings/SteamServer.asset"
      assert_includes stdout, "path=Project hotfix/Assets/ProjectHotfix/Runtime/RemoteProfile.cs"
      assert_includes stdout, "path=Project hotfix/Assets/ProjectHotfix/Runtime/RemoteTexture.cs"
      assert_includes stdout, "path=Project hotfix/Assets/ProjectHotfix/Runtime/SteamDedicated.cs"
      assert_includes stdout, "path=server/Program.cs"
      assert_includes stdout, "path=tools/tests/integration.sh"
      refute_includes stdout, secret
      refute_includes stderr, secret
      assert_includes stdout, "FINAL_RESULT=FAIL"
    end
  end

  def test_every_package_rule_rejects_keys_or_alias_values
    manifest = {
      "dependencies" => {
        "com.unity.services.relay" => "1.0.0",
        "safe-backend-alias" => "git+https://example.invalid/PlayFabSDK.git",
        "safe-database-alias" => "git+https://example.invalid/Npgsql.git",
        "com.unity.dedicated-server" => "1.0.0",
      },
    }
    with_repository(
      "Project hotfix/Packages/manifest.json" => JSON.generate(manifest),
      "Project hotfix/Packages/packages-lock.json" => JSON.generate(DEFAULT_PACKAGE_DOCUMENT)
    ) do |root|
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      %w[UNITY_HOSTED_BACKEND_PACKAGE THIRD_PARTY_BACKEND_PACKAGE DATABASE_PACKAGE DEDICATED_SERVER_PACKAGE].each do |rule|
        assert_includes stdout, "rule=#{rule}"
      end
      refute_includes stdout, "safe-backend-alias"
      assert_includes stdout, "FINAL_RESULT=FAIL"
    end
  end

  def test_policy_rule_ids_are_all_exercised_by_negative_fixtures
    policy = YAML.load_file(POLICY).fetch("ForbiddenInfrastructurePolicy")
    artifact = policy.fetch("artifactRules")
    rules = %w[
      forbiddenBasenameGroups
      forbiddenDirectorySegmentGroups
      forbiddenExtensionGroups
      forbiddenPathPatterns
      forbiddenFileNamePatterns
    ].flat_map { |key| artifact.fetch(key) }
    rules += policy.fetch("contentScan").fetch("rules")
    rules += policy.fetch("packageScan").fetch("rules")

    assert_equal EXERCISED_RULE_IDS, rules.map { |rule| rule.fetch("id") }.to_set
  end

  def test_tracked_missing_file_fails_closed
    with_repository("safe.txt" => "tracked\n") do |root|
      File.delete(root.join("safe.txt"))
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=TRACKED_FILE_MISSING"
      assert_includes stdout, "path=safe.txt"
    end
  end

  def test_required_package_manifests_cannot_be_omitted
    with_repository({ "safe.txt" => "tracked\n" }, include_packages: false) do |root|
      stdout, _stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=PACKAGE_MANIFEST_MISSING"
      assert_includes stdout, "AUDIT_VIOLATIONS=2"
    end
  end

  def test_operational_symlink_fails_without_following_or_printing_target
    target_secret = "outside-target-secret-fdn009"
    with_repository({}, symlinks: { "ops/runner" => "docker build #{target_secret}\n" }) do |root|
      stdout, stderr, status = run_verifier(root)

      assert_equal 1, status
      assert_includes stdout, "rule=UNSCANNABLE_SYMLINK"
      assert_includes stdout, "path=ops/runner"
      refute_includes stdout, target_secret
      refute_includes stderr, target_secret
    end
  end

  def test_oversize_invalid_utf8_and_invalid_package_inputs_fail_closed
    with_repository("ops/large.sh" => "x" * (MAX_SCAN_BYTES + 1)) do |root|
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=CONTENT_FILE_TOO_LARGE"
    end

    with_repository("ops/invalid.py" => "\xff".b) do |root|
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=CONTENT_FILE_INVALID_UTF8"
    end

    with_repository("Project hotfix/Packages/manifest.json" => " " * (MAX_SCAN_BYTES + 1)) do |root|
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=PACKAGE_MANIFEST_TOO_LARGE"
    end

    with_repository("Project hotfix/Packages/manifest.json" => "{ invalid json") do |root|
      stdout, _stderr, status = run_verifier(root)
      assert_equal 1, status
      assert_includes stdout, "rule=INVALID_PACKAGE_MANIFEST"
    end
  end

  def test_git_ignored_unity_cache_and_build_output_are_outside_inventory
    with_repository(
      ".gitignore" => "Project hotfix/Library/\nProject hotfix/Builds/\n",
      "Project hotfix/Library/PackageCache/Dockerfile" => "ignored\n",
      "Project hotfix/Builds/DedicatedServer/DedicatedServer.x86_64" => "ignored\n"
    ) do |root|
      stdout, stderr, status = run_verifier(root)

      assert_equal 0, status, stderr + stdout
      assert_includes stdout, "INVENTORY_FILES=3"
      assert_includes stdout, "FINAL_RESULT=PASS"
    end
  end

  def test_policy_cannot_disable_repository_wide_coverage
    policy = YAML.load_file(POLICY)
    policy.fetch("ForbiddenInfrastructurePolicy").fetch("contentScan")["roots"] = []

    Dir.mktmpdir("fdn009-policy-") do |directory|
      policy_path = Pathname.new(directory).join("invalid-policy.yaml")
      File.write(policy_path, YAML.dump(policy))
      stdout, stderr, status = run_verifier(REPOSITORY_ROOT, policy_path)

      assert_equal 2, status
      assert_empty stdout
      assert_includes stderr, "reason=INVALID_POLICY"
    end
  end

  def test_non_git_root_is_a_usage_error
    Dir.mktmpdir("fdn009-no-git-") do |root|
      _stdout, stderr, status = run_verifier(Pathname.new(root))

      assert_equal 2, status
      assert_includes stderr, "reason=GIT_INVENTORY_FAILED"
    end
  end

  private

  def with_repository(files, include_packages: true, symlinks: {}, executables: [])
    Dir.mktmpdir("fdn009-fixture-") do |directory|
      root = Pathname.new(directory)
      _stdout, stderr, status = Open3.capture3("git", "-C", root.to_s, "init", "-q")
      raise stderr unless status.success?

      baseline = {}
      if include_packages
        package_json = JSON.generate(DEFAULT_PACKAGE_DOCUMENT)
        baseline["Project hotfix/Packages/manifest.json"] = package_json
        baseline["Project hotfix/Packages/packages-lock.json"] = package_json
      end
      baseline.merge(files).each do |relative, content|
        path = root.join(relative)
        FileUtils.mkdir_p(path.dirname)
        File.binwrite(path, content)
      end
      symlinks.each_with_index do |(relative, content), index|
        target = root.join(".git", "fdn009-symlink-target-#{index}")
        File.binwrite(target, content)
        link = root.join(relative)
        FileUtils.mkdir_p(link.dirname)
        File.symlink(target, link)
      end
      executables.each { |relative| File.chmod(0o755, root.join(relative)) }

      _stdout, stderr, status = Open3.capture3("git", "-C", root.to_s, "add", "-A", "--", ".")
      raise stderr unless status.success?

      yield root
    end
  end

  def run_verifier(root, policy_path = POLICY)
    stdout, stderr, status = Open3.capture3(
      RbConfig.ruby,
      VERIFIER.to_s,
      "--root",
      root.to_s,
      "--policy",
      policy_path.to_s
    )
    [stdout, stderr, status.exitstatus]
  end
end
