import assert from "node:assert/strict";
import {
  emitterNodeType,
  raylibEffekseerProfileContract,
  requiredEmitterProfile,
  validateEmitterProjectParameters,
  validateRaylibEffekseerProfileContract
} from "../../../scripts/raylib-effekseer-profile-contracts.mjs";
import {
  effekseerRuntimeContract
} from "../../../scripts/effekseer-runtime-contract.mjs";

for (const [assetKind, usages] of Object.entries(raylibEffekseerProfileContract.assetKinds)) {
  assert.equal(emitterNodeType(assetKind), effekseerRuntimeContract.emitterNodeTypes[assetKind]);
  for (const [usage, profileId] of Object.entries(usages)) {
    assert.equal(requiredEmitterProfile(assetKind, usage), profileId);
  }
}

for (const [profileId, profile] of Object.entries(raylibEffekseerProfileContract.profiles)) {
  const project = { profile: profileId };
  for (const [parameter, typeId] of Object.entries(profile.parameters)) {
    const type = raylibEffekseerProfileContract.parameterTypes[typeId];
    project[parameter] = type.kind === "number"
      ? (type.minimum + type.maximum) / 2
      : `textures/contract-test${type.extension}`;
  }
  assert.deepEqual(
    validateEmitterProjectParameters(project, `test.${profileId}`),
    project);

  const missing = { ...project };
  delete missing[Object.keys(profile.parameters)[0]];
  assert.throws(
    () => validateEmitterProjectParameters(missing, `test.${profileId}`),
    /fields must be exactly/);

  assert.throws(
    () => validateEmitterProjectParameters({ ...project, unexpected: true }, `test.${profileId}`),
    /fields must be exactly/);
}

const extraRootField = structuredClone(raylibEffekseerProfileContract);
extraRootField.unexpected = true;
assert.throws(
  () => validateRaylibEffekseerProfileContract(extraRootField),
  /fields must be exactly/);

const missingVisualField = structuredClone(raylibEffekseerProfileContract);
delete missingVisualField.profiles.ring_pulse.visual.vertexCount;
assert.throws(
  () => validateRaylibEffekseerProfileContract(missingVisualField),
  /fields must be exactly/);

const unknownProfileReference = structuredClone(raylibEffekseerProfileContract);
unknownProfileReference.assetKinds.SpriteEmitter.primitive = "missing_profile";
assert.throws(
  () => validateRaylibEffekseerProfileContract(unknownProfileReference),
  /references unknown profile/);

assert.throws(
  () => validateEmitterProjectParameters({
    profile: "sprite_icon",
    size: 1,
    texture: "textures\\not-portable.png"
  }, "test.resource"),
  /portable relative resource path/);

process.stdout.write("Raylib Effekseer project profile contract tests passed.\n");
