-- Lua 5.4: identical poison-tick workload.
local N = tonumber(arg[1]) or 2000
local T = tonumber(arg[2]) or 200

local health, maxHealth, poisoned = {}, {}, {}

local function build()
    for i = 0, N - 1 do
        local mh = 50 + (i % 100)
        maxHealth[i] = mh
        health[i] = ((i * 37) % math.floor(mh)) + 1
        poisoned[i] = (i % 3 == 0) and 1 or 0
    end
end

local checksum, totalDamage = 0, 0
local function tick()
    for i = 0, N - 1 do
        local mh = maxHealth[i]
        if poisoned[i] ~= 0 then
            local dmg = math.min(math.max(mh * 0.05, 1), 25)  -- Clamp(mh*0.05,1,25)
            health[i] = health[i] - dmg
            if health[i] <= 0 then poisoned[i] = 0; health[i] = 0 end
            totalDamage = totalDamage + dmg
        elseif health[i] <= mh * 0.30 then
            local regen = math.min(mh * 0.02, mh - health[i])
            health[i] = health[i] + regen
        end
        checksum = checksum + health[i]
    end
end

-- warmup
build()
for _ = 1, 3 do tick() end
-- timed
build()
checksum, totalDamage = 0, 0
local t0 = os.clock()
for _ = 1, T do tick() end
local elapsed = os.clock() - t0

local ops = N * T
print(string.format("lua\t%d\t%d\t%.1f\t%.3f\t%.0f\t%.0f",
    N, T, elapsed * 1000, ops / elapsed / 1e6, checksum, totalDamage))
