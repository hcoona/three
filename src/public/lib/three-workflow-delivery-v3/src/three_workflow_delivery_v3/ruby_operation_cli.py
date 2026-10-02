"""Fixed hosted Ruby operation stages; no dispatch or configuration command."""

from __future__ import annotations

import argparse
import os
import subprocess
from pathlib import Path
from typing import cast

from three_workflow_delivery_v3._ruby_native import (
    ruby_object,
    ruby_text,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_contract import (
    require_bootstrap,
    ruby_bootstrap_source_digest,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_execution import (
    execute_ruby_bootstrap_publication,
    verify_ruby_bootstrap_remote,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_preparation import (
    build_ruby_bootstrap,
    provide_ruby_bootstrap,
    run_ruby_bootstrap_quality,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_publication import (
    RubyBootstrapAuthorization,
    RubyBootstrapMarker,
    observe_ruby_bootstrap_absence,
    render_ruby_bootstrap_summary,
)
from three_workflow_delivery_v3.acceptance.ruby_bootstrap_runtime import (
    collect_ruby_bootstrap_approval,
)
from three_workflow_delivery_v3.adapters.ruby import (
    RubyDistribution,
    build_ruby_package,
    qualify_ruby_consumer,
)
from three_workflow_delivery_v3.adapters.ruby_registry import RubyRegistryWriter
from three_workflow_delivery_v3.canonical import (
    JsonValue,
    canonicalize,
    parse_json_strict,
)
from three_workflow_delivery_v3.platform.ruby_github import (
    obtain_ruby_oidc_assertion,
)
from three_workflow_delivery_v3.platform.ruby_operation_http import (
    RubyOperationHttpsTransport,
)
from three_workflow_delivery_v3.records.release import ReleaseIntent
from three_workflow_delivery_v3.records.release_transport import (
    ReleaseAdmissionBindings,
)
from three_workflow_delivery_v3.records.ruby import (
    RubyArtifact,
    validate_ruby_quality_detail,
)
from three_workflow_delivery_v3.release.ruby_execution import (
    execute_ruby_publication,
)
from three_workflow_delivery_v3.release.ruby_finalizer import (
    RubyExactSatisfiedProof,
    RubyFinalizationInputs,
    finalize_ruby_attempt_outcome,
    ruby_exact_proof_from_document,
)
from three_workflow_delivery_v3.release.ruby_governance import (
    RUBY_WORKFLOW,
    RubyGovernance,
    ruby_publisher_job,
)
from three_workflow_delivery_v3.release.ruby_publication import (
    RubyApprovalBundle,
    RubyMutationMarker,
    RubyPublicationAuthorization,
    RubyPublicationSnapshot,
    RubyRemoteObservation,
    render_ruby_approval_summary,
)
from three_workflow_delivery_v3.release.ruby_qualification import (
    RubyQualificationSnapshot,
    qualify_ruby_release,
)
from three_workflow_delivery_v3.release.ruby_readback import (
    replay_ruby_observation,
)
from three_workflow_delivery_v3.repository.compiler import provider_binding
from three_workflow_delivery_v3.repository.node_provider import (
    CheckoutMaterialization,
    _run_command,
)
from three_workflow_delivery_v3.repository.ruby_model import (
    RUBY_QUALITY,
    admit_ruby_provider_facts,
    compile_ruby_repository_model,
    ruby_provider_manifest,
)
from three_workflow_delivery_v3.repository.ruby_provider import (
    provide_ruby_repository_facts,
)
from three_workflow_delivery_v3.ruby_operation_host import (
    HostedRubyArtifacts,
    admit_operation_input,
    now,
    outputs,
    write,
)
from three_workflow_delivery_v3.ruby_operation_records import (
    RubyOperationRecords,
    instant,
    same,
)
from three_workflow_delivery_v3.ruby_operation_runtime import (
    HostedRubyGitHub,
    PhaseReservation,
    authority_guard,
    initialize_spending,
    phase_claim,
    registry_reader,
)


def _provider(inputs: HostedRubyArtifacts, output: Path) -> None:
    operation = inputs.operation()
    if operation.bootstrap:
        output.write_bytes(
            provide_ruby_bootstrap(
                inputs.root,
                operation.bootstrap_run(now()),
                CheckoutMaterialization(0, credentials_persisted=False),
            )
        )
        return
    manifest = ruby_provider_manifest(operation.context())
    same(inputs.document("provider-request"), manifest.to_document())
    result = provide_ruby_repository_facts(
        inputs.root,
        provider_binding(manifest, "ruby-smoke"),
        CheckoutMaterialization(0, credentials_persisted=False),
    )
    same(result.nbgv.to_document(), operation.request.document["nbgv"])
    same(
        ruby_bootstrap_source_digest(result),
        operation.request.document["source-manifest-digest"],
    )
    require_bootstrap(
        _run_command(
            ("git", "rev-parse", operation.target + "^{tree}"), inputs.root
        ).strip()
        == operation.request.document["tree"],
        "Ruby source tree differs from the exact request",
    )
    operation.require_current(now())
    write(output, result.to_document())


def _compile(inputs: HostedRubyArtifacts, output: Path) -> None:
    operation = inputs.operation()
    require_bootstrap(
        not operation.bootstrap, "Bootstrap uses its distinct Plan"
    )
    manifest = ruby_provider_manifest(operation.context())
    facts = admit_ruby_provider_facts(
        inputs.content("provider"),
        manifest=manifest,
        request_reference=inputs.reference("provider-request"),
        result_reference=inputs.reference("provider"),
        result_transport=inputs.transport("provider"),
    )
    same(facts.result.nbgv.to_document(), operation.request.document["nbgv"])
    same(
        ruby_bootstrap_source_digest(facts.result),
        operation.request.document["source-manifest-digest"],
    )
    write(
        output, compile_ruby_repository_model(inputs.root, facts).to_document()
    )


def _plan(records: RubyOperationRecords, output: Path) -> None:
    operation, inputs = records.operation, records.inputs
    if operation.bootstrap:
        write(output, records.bootstrap_plan().to_document())
        return
    intent = ReleaseIntent(
        "hcoona/three",
        RUBY_WORKFLOW,
        "refs/heads/main",
        operation.target,
        operation.request_id,
        "hcoona",
        operation.run_id,
        "workflow_dispatch",
        "refs/heads/main",
        operation.target,
        operation.registry.channel,
        "live",
        "live-release",
        "hcoona-release-smoke-ruby",
    )
    plan = RubyQualificationSnapshot(
        intent,
        records.model(),
        inputs.reference("model"),
        operation.governance(now()),
    )
    write(output, plan.to_document())


def _build(records: RubyOperationRecords) -> None:
    inputs = records.inputs
    result = (
        build_ruby_bootstrap(inputs.root, records.bootstrap_plan())
        if records.operation.bootstrap
        else build_ruby_package(inputs.root, records.model().build_request())
    )
    records.operation.require_current(now())
    path = inputs.directory / result.filename
    path.write_bytes(result.content)
    outputs({"gem-path": str(path)})


def _artifact(records: RubyOperationRecords, output: Path) -> None:
    inputs = records.inputs
    witness = (
        records.bootstrap_plan().build_request().witness
        if records.operation.bootstrap
        else records.model().build_request().witness
    )
    artifact = RubyArtifact(
        inputs.reference("gem").payload_path,
        len(inputs.content("gem")),
        witness,
        inputs.reference("gem"),
        inputs.transport("gem"),
    )
    artifact.inspect(inputs.content("gem"))
    write(output, artifact.to_document())


def _quality(records: RubyOperationRecords, output: Path) -> None:
    artifact, payload = records.artifact(), records.inputs.content("gem")
    if records.operation.bootstrap:
        evidence = [
            run_ruby_bootstrap_quality(
                records.bootstrap_plan(), artifact, payload, definition
            ).to_document()
            for definition in RUBY_QUALITY
        ]
    else:
        evidence = [
            item.to_document()
            for item in qualify_ruby_release(
                records.normal_plan(), artifact, payload
            )
        ]
    records.operation.require_current(now())
    write(
        output,
        {
            "schema": "workflow-delivery/v3/ruby-hosted-quality-set-v1",
            "items": cast("JsonValue", evidence),
        },
    )


def _observe(records: RubyOperationRecords, output: Path) -> None:
    inputs, operation = records.inputs, records.operation
    token = os.environ["GITHUB_TOKEN"]
    if operation.bootstrap:
        publication = records.bootstrap_publication()
        github = HostedRubyGitHub(inputs, token)
        absence = observe_ruby_bootstrap_absence(
            publication,
            registry_reader(inputs, "eligibility", token),
            phase="eligibility",
            clock=now,
            check_authority=authority_guard(inputs, github),
        )
        write(output, absence.to_document())
    else:
        decision = records.normal_qualification()
        require_bootstrap(
            decision.result == "passed",
            "Ruby normal registry reads require passed Qualification",
        )
        github = HostedRubyGitHub(inputs, token)
        authority_guard(inputs, github, decision.snapshot.governance)()
        reader = registry_reader(inputs, "eligibility", token)
        native = reader.observe(
            decision.artifact.inspect(inputs.content("gem"))
        )
        write(
            output,
            RubyRemoteObservation(
                decision, inputs.reference("qualification"), native, now()
            ).to_document(),
        )


def _publication(records: RubyOperationRecords, output: Path) -> None:
    snapshot = RubyPublicationSnapshot(
        records.normal_observation(), records.inputs.reference("observation")
    )
    write(output, snapshot.to_document())
    outputs({"action-required": str(snapshot.action_required).lower()})


def _summary(records: RubyOperationRecords, output: Path) -> None:
    content = (
        render_ruby_bootstrap_summary(records.bootstrap_absence())
        if records.operation.bootstrap
        else render_ruby_approval_summary(records.normal_publication())
    )
    output.write_bytes(content)
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with Path(summary).open("ab") as stream:
            stream.write(content)


def _bundle(records: RubyOperationRecords, output: Path) -> None:
    inputs = records.inputs
    if records.operation.bootstrap:
        write(output, records.bootstrap_bundle().to_document())
    else:
        write(
            output,
            RubyApprovalBundle(
                records.normal_publication(),
                inputs.reference("publication"),
                inputs.reference("summary"),
            ).to_document(),
        )


def _authorize(records: RubyOperationRecords, output: Path) -> None:
    inputs, operation = records.inputs, records.operation
    github = HostedRubyGitHub(inputs, os.environ["GITHUB_TOKEN"])
    sentinel = os.environ["WDV3_ENVIRONMENT_SENTINEL"]
    if operation.bootstrap:
        publication = records.bootstrap_publication()
        authority_guard(inputs, github)()
        proof = collect_ruby_bootstrap_approval(
            publication.run, github, sentinel=sentinel, now=now()
        )
        authority = RubyBootstrapAuthorization(
            records.bootstrap_bundle(),
            inputs.reference("bundle"),
            inputs.transport("bundle"),
            proof,
            now(),
        )
        write(output, authority.to_document())
    else:
        decision = records.normal_qualification()
        authority_guard(inputs, github, decision.snapshot.governance)()
        proof = github.approval(
            decision.snapshot.intent,
            decision.snapshot.governance,
            sentinel=sentinel,
        )
        write(
            output,
            RubyPublicationAuthorization(
                records.normal_bundle(),
                inputs.reference("bundle"),
                proof,
                now(),
            ).to_document(),
        )


def _marker(records: RubyOperationRecords, output: Path) -> None:
    inputs, operation = records.inputs, records.operation
    token = os.environ["GITHUB_TOKEN"]
    github = HostedRubyGitHub(inputs, token)
    reader = registry_reader(inputs, "pre-marker", token)
    if operation.bootstrap:
        auth = records.bootstrap_authorization()
        absence = observe_ruby_bootstrap_absence(
            records.bootstrap_publication(),
            reader,
            phase="pre-marker",
            clock=now,
            check_authority=authority_guard(inputs, github),
            authorization=auth,
        )
        write(
            output,
            RubyBootstrapMarker(
                auth,
                inputs.reference("authorization"),
                inputs.transport("authorization"),
                absence,
            ).to_document(),
        )
    else:
        auth = records.normal_authorization()
        current = authority_guard(
            inputs,
            github,
            auth.bundle.snapshot.observation.decision.snapshot.governance,
        )()
        fresh = RubyGovernance(
            operation.registry,
            canonicalize(current["governance"]),
            ruby_text(current["source-commit"]),
            instant(current["observed-at"]),
        )
        native = reader.observe(
            records.artifact().inspect(inputs.content("gem"))
        )
        write(
            inputs.directory / "fresh-governance.json",
            {
                "content": fresh.document,
                "source-commit": fresh.source_commit,
                "observed-at": fresh.observed_at.isoformat(),
            },
        )
        write(
            output,
            RubyMutationMarker(
                auth, inputs.reference("authorization"), fresh, native, now()
            ).to_document(),
        )


def _execute(records: RubyOperationRecords, output: Path) -> None:
    inputs, operation = records.inputs, records.operation
    token = os.environ["GITHUB_TOKEN"]
    lease = PhaseReservation(inputs, "execute")
    reader = registry_reader(inputs, "execute", token)
    github = HostedRubyGitHub(inputs, token)
    oidc = RubyOperationHttpsTransport(lease.deadline)
    if operation.bootstrap:
        result = execute_ruby_bootstrap_publication(
            records.bootstrap_marker(),
            inputs.reference("marker"),
            inputs.transport("marker"),
            reader=reader,
            claim_path=inputs.directory / "upload-claim.json",
            deadline=lease.deadline,
            clock=now,
            check_authority=authority_guard(inputs, github),
            assertion=(
                lambda: obtain_ruby_oidc_assertion(
                    "rubygems.org", os.environ, oidc
                )
            )
            if operation.registry.name == "rubygems"
            else None,
            github_token=token
            if operation.registry.name == "github-packages"
            else None,
        )
        write(output, result.to_document())
        outputs({"publication-result": result.result})
        return
    marker = records.normal_marker()
    guard = authority_guard(inputs, github, marker.fresh_governance)
    write(
        inputs.directory / "credential-claim.json",
        {"marker-reference": inputs.reference("marker").to_document()},
        exclusive=True,
    )
    write(
        inputs.directory / "credential-authority.json",
        guard(),
        exclusive=True,
    )
    writer = RubyRegistryWriter(operation.registry, reader.budget)
    if operation.registry.name == "rubygems":
        assertion = obtain_ruby_oidc_assertion("rubygems.org", os.environ, oidc)
        operation.require_current(now())
        try:
            credential = writer.exchange(assertion, now=now())
        finally:
            del assertion
            if writer.exchange_receipt is not None:
                write(
                    inputs.directory / "exchange-receipt.json",
                    writer.exchange_receipt,
                    exclusive=True,
                )
    else:
        credential = token
    write(
        inputs.directory / "upload-authority.json",
        guard(),
        exclusive=True,
    )
    result = execute_ruby_publication(
        marker,
        inputs.reference("marker"),
        inputs.content("gem"),
        credential=credential,
        reader=reader,
        writer=writer,
        claim_path=inputs.directory / "upload-claim.json",
        deadline=lease.deadline,
        clock=now,
    )
    write(output, result.to_document())
    outputs({"publication-result": result.result})


def _zero(records: RubyOperationRecords, output: Path) -> None:
    inputs, operation = records.inputs, records.operation
    require_bootstrap(
        not operation.bootstrap,
        "Bootstrap cannot become a zero-action normal release",
    )
    publication = records.normal_publication()
    require_bootstrap(
        not publication.action_required,
        "Ruby zero-action proof has an upload action",
    )
    token = os.environ["GITHUB_TOKEN"]
    github = HostedRubyGitHub(inputs, token)
    current = authority_guard(
        inputs, github, publication.observation.decision.snapshot.governance
    )()
    fresh = RubyGovernance(
        operation.registry,
        canonicalize(current["governance"]),
        ruby_text(current["source-commit"]),
        instant(current["observed-at"]),
    )
    native = registry_reader(inputs, "zero-action", token).observe(
        records.artifact().inspect(inputs.content("gem"))
    )
    proof = RubyExactSatisfiedProof(
        publication, inputs.reference("publication"), fresh, native, now()
    )
    write(
        inputs.directory / "zero-governance.json",
        {
            "content": fresh.document,
            "source-commit": fresh.source_commit,
            "observed-at": fresh.observed_at.isoformat(),
        },
    )
    write(output, proof.to_document())


def _normal_finalization(
    records: RubyOperationRecords, needs: dict[str, JsonValue]
) -> JsonValue:
    inputs, operation = records.inputs, records.operation
    decision = records.normal_qualification()
    bindings = RubyFinalizationInputs(
        decision, inputs.reference("qualification"), inputs.content("gem")
    )
    from dataclasses import replace  # noqa: PLC0415

    if "observation" in inputs.references:
        bindings = replace(
            bindings,
            observation=(
                records.normal_observation(),
                inputs.reference("observation"),
            ),
        )
    if "publication" in inputs.references:
        bindings = replace(
            bindings,
            publication=(
                records.normal_publication(),
                inputs.reference("publication"),
            ),
        )
    if "bundle" in inputs.references:
        bindings = replace(
            bindings,
            bundle=(records.normal_bundle(), inputs.reference("bundle")),
        )
    if "authorization" in inputs.references:
        bindings = replace(
            bindings,
            authorization=(
                records.normal_authorization(),
                inputs.reference("authorization"),
            ),
        )
    if "result" in inputs.references:
        bindings = replace(
            bindings,
            terminal=(records.normal_result(), inputs.reference("result")),
            result_marker=(records.normal_marker(), inputs.reference("marker")),
        )
    elif "marker" in inputs.references:
        bindings = replace(
            bindings,
            terminal=(records.normal_marker(), inputs.reference("marker")),
        )
    if "exact-proof" in inputs.references:
        doc = inputs.document("zero-governance")
        fresh = RubyGovernance(
            operation.registry,
            canonicalize(doc["content"]),
            ruby_text(doc["source-commit"]),
            instant(doc["observed-at"]),
        )
        proof = ruby_exact_proof_from_document(
            inputs.document("exact-proof"),
            records.normal_publication(),
            fresh,
            inputs.content("gem"),
        )
        bindings = replace(
            bindings, exact_proof=(proof, inputs.reference("exact-proof"))
        )
    publisher = ruby_object(needs[ruby_publisher_job(operation.registry)])
    native_outputs = ruby_object(publisher["outputs"])
    outcome = finalize_ruby_attempt_outcome(
        bindings,
        current=ReleaseAdmissionBindings(
            "live-release", operation.run_id, None, operation.target
        ),
        run_attempt=int(os.environ["GITHUB_RUN_ATTEMPT"]),
        publisher_conclusion=ruby_text(publisher["result"]),
        publication_step_outcome=cast(
            "str | None", native_outputs.get("publication-step-outcome")
        ),
        publication_terminal_reference=cast(
            "str | None", native_outputs.get("terminal-reference")
        ),
        observation_conclusion=ruby_text(
            ruby_object(needs["prepare-ruby-publication"])["result"]
        ),
    )
    return None if outcome is None else outcome.to_document()


def _finalize(inputs: HostedRubyArtifacts, output: Path) -> None:
    """Retain failure/unknown evidence when predecessors are absent."""
    needs = ruby_object(parse_json_strict(os.environ["WDV3_NEEDS"]))
    terminal: dict[str, JsonValue] = {
        "schema": "workflow-delivery/v3/ruby-hosted-terminal-v1",
        "run-id": int(os.environ["GITHUB_RUN_ID"]),
        "run-attempt": int(os.environ["GITHUB_RUN_ATTEMPT"]),
        "target": os.environ["GITHUB_SHA"],
        "result": "failed",
        "independent-audit": False,
        "needs": needs,
        "references": inputs.references,
        "normal-live-completion": False,
    }
    try:
        records = RubyOperationRecords(inputs)
        if records.operation.bootstrap:
            publisher = ruby_object(
                needs[
                    ruby_publisher_job(
                        records.operation.registry, bootstrap=True
                    )
                ]
            )
            if "result" in inputs.references:
                result = records.bootstrap_result()
                terminal["bootstrap-result"] = result.to_document()
                if (
                    result.result == "published"
                    and publisher["result"] == "success"
                ):
                    terminal["result"] = "bootstrap-published"
            elif "marker" in inputs.references:
                records.bootstrap_marker()
                terminal["result"] = "unknown"
        else:
            outcome = _normal_finalization(records, needs)
            if outcome is not None:
                write(inputs.directory / "outcome.json", outcome)
                terminal["outcome"] = outcome
                terminal["result"] = ruby_object(outcome)["disposition"]
    except (
        OSError,
        ValueError,
        TypeError,
        KeyError,
        RuntimeError,
        subprocess.SubprocessError,
    ) as error:
        terminal["error-kind"] = type(error).__name__
        if "marker" in inputs.references:
            terminal["result"] = "unknown"
    write(output, terminal)
    outputs(
        {
            "terminal-result": ruby_text(terminal["result"]),
            "outcome-created": str("outcome" in terminal).lower(),
        }
    )


def _remote(records: RubyOperationRecords, output: Path) -> None:
    inputs, operation = records.inputs, records.operation
    token = os.environ["GITHUB_TOKEN"]
    github = HostedRubyGitHub(inputs, token)
    reader = registry_reader(inputs, "remote-consumer", token)
    terminal = inputs.document("terminal")
    require_bootstrap(
        terminal["result"]
        in {"bootstrap-published", "published", "exact-satisfied"},
        "Ruby remote consumer cannot repair a failed operation",
    )
    if operation.bootstrap:
        write(
            output,
            verify_ruby_bootstrap_remote(
                records.bootstrap_result(),
                reader,
                clock=now,
                check_authority=authority_guard(inputs, github),
            ),
        )
        return
    # Recompute the strict terminal outcome rather than accepting a scalar flag.
    same(
        terminal["outcome"],
        _normal_finalization(records, ruby_object(terminal["needs"])),
    )
    decision = records.normal_qualification()
    current = authority_guard(inputs, github, decision.snapshot.governance)()
    native = reader.observe(decision.artifact.inspect(inputs.content("gem")))
    replayed = replay_ruby_observation(
        native.to_document(),
        decision.artifact.inspect(inputs.content("gem")),
        operation.registry,
    )
    require_bootstrap(
        replayed.classification == "exact"
        and replayed.distribution is not None,
        "Ruby final remote original is not exact",
    )
    detail = qualify_ruby_consumer(
        cast("RubyDistribution", replayed.distribution)
    )
    validate_ruby_quality_detail(
        decision.artifact, RUBY_QUALITY[1], "passed", canonicalize(detail)
    )
    operation.require_current(now())
    write(
        output,
        {
            "schema": "workflow-delivery/v3/ruby-hosted-remote-consumer-v1",
            "outcome-reference": inputs.reference("outcome").to_document(),
            "native": replayed.to_document(),
            "consumer": detail,
            "authority": current,
            "completed-at": now().isoformat(),
            "independent-audit": False,
        },
    )


def main(argv: list[str] | None = None) -> int:  # noqa: C901, PLR0912, PLR0915
    """Execute one fixed native job stage without retry or service setup."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "command",
        choices=(
            "request",
            "initialize",
            "provider",
            "compile",
            "plan",
            "build",
            "artifacts",
            "quality",
            "qualification",
            "claim",
            "observe",
            "publication",
            "summary",
            "bundle",
            "authorize",
            "marker",
            "execute",
            "zero-proof",
            "finalize",
            "remote",
            "bind",
            "export",
            "audit",
            "upload-check",
        ),
    )
    parser.add_argument("--root", type=Path, default=Path())
    parser.add_argument("--directory", type=Path, default=Path(".wdv3"))
    parser.add_argument(
        "--output", type=Path, default=Path(".wdv3/output.json")
    )
    parser.add_argument("--role")
    parser.add_argument("--phase")
    parser.add_argument("--payload", type=Path)
    parser.add_argument("--artifact-id")
    parser.add_argument("--artifact-digest")
    parser.add_argument("--artifact-url")
    args = parser.parse_args(argv)
    try:
        args.directory.mkdir(parents=True, exist_ok=True)
        inputs = HostedRubyArtifacts(
            args.root, args.directory, os.environ.get("WDV3_REFERENCES", "{}")
        )
        if args.command == "request":
            operation = admit_operation_input(
                args.root, os.environ["WDV3_OPERATION_INPUT"]
            )
            require_bootstrap(
                operation.registry.name
                == os.environ["WDV3_SELECTED_DESTINATION"],
                "Ruby dispatch destination differs from its exact request",
            )
            write(args.output, operation.document)
            if not operation.bootstrap:
                write(
                    args.directory / "provider-request.json",
                    ruby_provider_manifest(operation.context()).to_document(),
                )
            outputs(
                {
                    "destination": operation.registry.name,
                    "environment": operation.registry.environment,
                }
            )
        elif args.command == "initialize":
            initialize_spending(args.directory / "github-spending")
        elif args.command == "upload-check":
            require_bootstrap(
                args.role not in inputs.references
                and args.payload.is_file()
                and not args.payload.is_symlink()
                and 0 < args.payload.stat().st_size <= 2 * 1024 * 1024
                and os.environ["GITHUB_JOB"] == inputs.producer(args.role),
                "Ruby upload lacks its exact bounded role and producer",
            )
        elif args.command == "bind":
            inputs.bind(
                args.role,
                args.payload,
                args.artifact_id,
                args.artifact_digest,
                args.artifact_url,
            )
        elif args.command == "export":
            inputs.export()
            role = (
                "result"
                if "result" in inputs.references
                else "marker"
                if "marker" in inputs.references
                else None
            )
            outputs(
                {
                    "terminal-reference": ""
                    if role is None
                    else canonicalize(inputs.references[role]).decode()
                }
            )
        elif args.command == "finalize":
            _finalize(inputs, args.output)
        elif args.command == "audit":
            evidence: dict[str, JsonValue] = {}
            for directory in (
                "github-spending",
                "github-responses",
                "spending",
                "upload-claim.json-observations",
            ):
                base = inputs.directory / directory
                for path in (
                    sorted(base.rglob("*.json")) if base.exists() else ()
                ):
                    evidence[str(path.relative_to(inputs.directory))] = (
                        parse_json_strict(path.read_bytes())
                    )
            for name in (
                "credential-claim.json",
                "exchange-receipt.json",
                "upload-claim.json",
                "credential-authority.json",
                "upload-authority.json",
                "github-spending-membership.json",
            ):
                path = inputs.directory / name
                if path.is_file():
                    evidence[name] = parse_json_strict(path.read_bytes())
            write(
                args.output,
                {
                    "schema": "workflow-delivery/v3/ruby-hosted-job-audit-v1",
                    "job": os.environ["GITHUB_JOB"],
                    "evidence": evidence,
                },
            )
        else:
            inputs.validate_all()
            operation = inputs.operation()
            if args.command == "provider":
                _provider(inputs, args.output)
            elif args.command == "compile":
                _compile(inputs, args.output)
            elif args.command == "claim":
                write(
                    args.output,
                    phase_claim(operation, args.phase),
                    exclusive=True,
                )
                initialize_spending(args.directory / "spending" / args.phase)
            else:
                records = RubyOperationRecords(inputs)
                stages = {
                    "plan": _plan,
                    "artifacts": _artifact,
                    "quality": _quality,
                    "observe": _observe,
                    "publication": _publication,
                    "summary": _summary,
                    "bundle": _bundle,
                    "authorize": _authorize,
                    "marker": _marker,
                    "execute": _execute,
                    "zero-proof": _zero,
                    "remote": _remote,
                }
                if args.command == "build":
                    _build(records)
                elif args.command == "qualification":
                    decision = (
                        records.bootstrap_qualification()
                        if operation.bootstrap
                        else records.normal_qualification()
                    )
                    write(args.output, decision.to_document())
                    outputs({"qualification-result": decision.result})
                else:
                    stages[args.command](records, args.output)
    except (
        OSError,
        ValueError,
        TypeError,
        KeyError,
        RuntimeError,
        subprocess.SubprocessError,
    ) as error:
        print(f"Ruby operation rejected: {type(error).__name__}")  # noqa: T201
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
