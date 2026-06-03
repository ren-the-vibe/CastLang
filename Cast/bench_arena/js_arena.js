"use strict";
const SIZE = 20, HCEIL = 32, CAP = 2000;

function run(n0, ticks, seed) {
    let s = seed >>> 0;
    const nxt = () => { s = (Math.imul(1664525, s) + 1013904223) >>> 0; return s; };
    const rint = (n) => nxt() % n;
    const rspan = () => (nxt() % 3) - 1;

    const px = new Float64Array(CAP), pz = new Float64Array(CAP), ph = new Float64Array(CAP);
    const vx = new Int32Array(CAP), vz = new Int32Array(CAP);
    const lineage = new Float64Array(CAP);
    const ancestors = new Array(CAP);
    const alive = new Uint8Array(CAP), isMage = new Uint8Array(CAP), cursed = new Uint8Array(CAP), oob = new Uint8Array(CAP);

    let N = 0, births = 0, deaths = 0, cursings = 0, live = 0, nextL = 1, cursedL = -1, mage = 0;

    function build() {
        N = 0; nextL = 1; births = 0; deaths = 0; cursings = 0; cursedL = -1;
        for (let k = 0; k < n0; k++) {
            const i = N++;
            px[i] = 2 + rint(SIZE - 4); pz[i] = 2 + rint(SIZE - 4); ph[i] = (HCEIL / 2) | 0;
            vx[i] = rspan(); vz[i] = rspan();
            lineage[i] = nextL++; ancestors[i] = [lineage[i]];
            alive[i] = 1; isMage[i] = 0; cursed[i] = 0; oob[i] = 0;
        }
        mage = N++;
        px[mage] = (SIZE / 2) | 0; pz[mage] = (SIZE / 2) | 0; ph[mage] = (HCEIL / 2) | 0;
        vx[mage] = rspan(); vz[mage] = rspan();
        lineage[mage] = nextL++; ancestors[mage] = [lineage[mage]];
        alive[mage] = 1; isMage[mage] = 1; cursed[mage] = 0; oob[mage] = 0;
        live = N;
    }
    const oobCheck = (i) => px[i] < 0 || px[i] >= SIZE || pz[i] < 0 || pz[i] >= SIZE || ph[i] > HCEIL;

    function ruleKillOob() {
        for (let i = 0; i < N; i++)
            if (alive[i] && oob[i] && !isMage[i]) { alive[i] = 0; deaths++; live--; }
    }
    function ruleBirth(p1, p2) {
        if (live > 400) return;
        if (N >= CAP) return;
        const c = N++;
        px[c] = (px[p1] + px[p2]) / 2; pz[c] = (pz[p1] + pz[p2]) / 2; ph[c] = (HCEIL / 2) | 0;
        vx[c] = rspan(); vz[c] = rspan();
        lineage[c] = nextL++;
        const anc = [lineage[c]];
        for (const a of ancestors[p1]) if (!anc.includes(a)) anc.push(a);
        for (const a of ancestors[p2]) if (!anc.includes(a)) anc.push(a);
        ancestors[c] = anc;
        alive[c] = 1; isMage[c] = 0; oob[c] = 0;
        cursed[c] = (cursedL >= 0 && anc.includes(cursedL)) ? 1 : 0;
        births++; live++;
    }
    function ruleCurse(off) {
        cursings++; cursedL = off;
        px[mage] = (SIZE / 2) | 0; pz[mage] = (SIZE / 2) | 0; ph[mage] = (HCEIL / 2) | 0;
        for (let i = 0; i < N; i++)
            if (alive[i] && ancestors[i].includes(off)) cursed[i] = 1;
    }
    function ruleStandingCurse() {
        if (cursedL < 0) return;
        for (let i = 0; i < N; i++)
            if (alive[i] && !cursed[i] && ancestors[i].includes(cursedL)) cursed[i] = 1;
    }

    function step() {
        const mid = (SIZE / 2) | 0;
        for (let i = 0; i < N; i++) {
            if (!alive[i]) continue;
            let sx = rspan(), sz = rspan();
            if (px[i] < mid - 6) sx += 1; else if (px[i] > mid + 6) sx -= 1;
            if (pz[i] < mid - 6) sz += 1; else if (pz[i] > mid + 6) sz -= 1;
            vx[i] += sx; vz[i] += sz;
            if (vx[i] > 1) vx[i] = 1; if (vx[i] < -1) vx[i] = -1;
            if (vz[i] > 1) vz[i] = 1; if (vz[i] < -1) vz[i] = -1;
            px[i] += vx[i]; pz[i] += vz[i];
        }
        for (let i = 0; i < N; i++) {
            if (!alive[i] || !oobCheck(i)) continue;
            if (isMage[i]) {
                px[i] = Math.min(Math.max(px[i], 1), SIZE - 2);
                pz[i] = Math.min(Math.max(pz[i], 1), SIZE - 2);
            } else oob[i] = 1;
        }
        const count = N;
        for (let i = 0; i < count; i++) {
            if (!alive[i] || oob[i]) continue;
            for (let j = i + 1; j < count; j++) {
                if (!alive[j] || oob[j]) continue;
                if (px[i] !== px[j] || pz[i] !== pz[j]) continue;
                const dirx = px[j] >= mid ? 1 : -1, dirz = pz[j] >= mid ? 1 : -1;
                px[j] += dirx * 2; pz[j] += dirz * 2;
                if (isMage[i] && oobCheck(i)) ruleCurse(lineage[j]);
                else if (isMage[j] && oobCheck(j)) ruleCurse(lineage[i]);
                else if (!isMage[i] && !isMage[j]) ruleBirth(i, j);
            }
        }
    }

    build();
    for (let t = 0; t < ticks; t++) {
        step(); ruleKillOob(); ruleStandingCurse();
        for (let i = 0; i < N; i++) oob[i] = 0;
    }
    let chk = 0;
    for (let i = 0; i < N; i++)
        if (alive[i]) chk += px[i] * 3 + pz[i] * 5 + ph[i] * 7 + lineage[i] * 11 + (cursed[i] ? 13 : 0);
    return { births, deaths, cursings, live, chk };
}

const N0 = process.argv[2] ? parseInt(process.argv[2]) : 30;
const Ticks = process.argv[3] ? parseInt(process.argv[3]) : 300;
const seed = process.argv[4] ? parseInt(process.argv[4]) : 12345;
run(N0, 10, seed); // warmup
const t0 = process.hrtime.bigint();
const r = run(N0, Ticks, seed);
const el = Number(process.hrtime.bigint() - t0) / 1e9;
console.log(`javascript\t${N0}\t${Ticks}\t${(el*1000).toFixed(1)}\t${r.births}\t${r.deaths}\t${r.cursings}\t${r.live}\t${r.chk.toFixed(0)}`);
