import {readFileSync} from 'node:fs';
// Release owns prompts, IDs, assertions and reporting. This adapter only consumes
// the public A/B projection; controller-held C prompts never enter this runner.
export function loadReleaseProfile(path) {
    if (!path) throw new Error('HONUA_FOSS4G_PROFILE_PROJECTION must identify the Release-generated Console projection.');
    const text=readFileSync(path,'utf8');
    if (Buffer.byteLength(text)>1024*1024) throw new Error('Release projection exceeds the bounded input size.');
    const value=JSON.parse(text);
    if (value.schemaVersion!=='honua.foss4g-pilot-projection/v1' || value.entrypoint!=='console' || !Array.isArray(value.scenarios)
        || !/^sha256:[a-f0-9]{64}$/.test(value.profileDigest)) throw new Error('Invalid Release pilot projection.');
    return value;
}
export function releasePrompt(profile,scenarioId,subcaseId,variant='A') {
    if (!['A','B'].includes(variant)) throw new Error('This development runner accepts Release A/B variants only.');
    const scenario=profile.scenarios.find(value=>value.id===scenarioId);
    const subcase=scenario?.subcases.find(value=>value.id===subcaseId)
        || scenario?.subcases.map(value=>value.developmentProbe).find(value=>value?.id===subcaseId);
    const prompt=subcase?.prompts?.[variant];
    if (typeof prompt!=='string' || !prompt.trim()) throw new Error(`Missing Release prompt ${scenarioId}:${subcaseId}:${variant}.`);
    return prompt;
}
