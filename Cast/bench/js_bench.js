"use strict";
const N = process.argv[2] ? parseInt(process.argv[2]) : 2000;
const T = process.argv[3] ? parseInt(process.argv[3]) : 200;

const health = new Float64Array(N);
const maxHealth = new Float64Array(N);
const poisoned = new Float64Array(N);

function build() {
    for (let i = 0; i < N; i++) {
        const mh = 50 + (i % 100);
        maxHealth[i] = mh;
        health[i] = ((i * 37) % Math.trunc(mh)) + 1;
        poisoned[i] = (i % 3 === 0) ? 1 : 0;
    }
}

let checksum = 0, totalDamage = 0;
function tick() {
    for (let i = 0; i < N; i++) {
        const mh = maxHealth[i];
        if (poisoned[i] !== 0) {
            const dmg = Math.min(Math.max(mh * 0.05, 1), 25); // Clamp(mh*0.05,1,25)
            health[i] -= dmg;
            if (health[i] <= 0) { poisoned[i] = 0; health[i] = 0; }
            totalDamage += dmg;
        } else if (health[i] <= mh * 0.30) {
            const regen = Math.min(mh * 0.02, mh - health[i]);
            health[i] += regen;
        }
        checksum += health[i];
    }
}

// warmup
build();
for (let i = 0; i < 3; i++) tick();
// timed
build();
checksum = 0; totalDamage = 0;
const t0 = process.hrtime.bigint();
for (let t = 0; t < T; t++) tick();
const elapsed = Number(process.hrtime.bigint() - t0) / 1e9;

const ops = N * T;
console.log(`javascript\t${N}\t${T}\t${(elapsed*1000).toFixed(1)}\t${(ops/elapsed/1e6).toFixed(3)}\t${checksum.toFixed(0)}\t${totalDamage.toFixed(0)}`);
