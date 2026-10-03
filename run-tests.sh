#!/bin/sh
# run-tests.sh — local build + test runner for Linux/macOS (issue #13).
#
# Does EXACTLY what .github/workflows/ci.yml does, so a contributor on a
# Unix box can run the same pipeline locally instead of only in CI:
#
#   sh run-tests.sh        # compile game + tests, run the unit suite, run the smoke tests
#
# Exit code 0 = all green, non-zero = a step failed.
#
# Requires the Mono toolchain (mcs + mono), as in CI:
#   sudo apt-get install -y mono-complete
#
# Windows: see run-tests.ps1 (MSBuild / Visual Studio). The Makefile in the
# repo root is a thin wrapper around this script (`make test`).

set -u

# Run from the repo root no matter where it is invoked from.
cd "$(dirname "$0")" || exit 1

GAME_SOURCES="BasicGuessingGame/Program.cs BasicGuessingGame/GameEngine.cs"
TEST_SOURCES="BasicGuessingGame.Tests/GameEngineTests.cs BasicGuessingGame/GameEngine.cs"
GAME_BIN="BasicGuessingGame/bin/Guess.exe"
TEST_BIN="BasicGuessingGame.Tests/bin/GameEngineTests.exe"

step() { printf '\n==> %s\n' "$1"; }
fail() { printf 'FAIL: %s\n' "$1" >&2; exit 1; }

command -v mcs  >/dev/null 2>&1 || fail "mcs (Mono) not found — install mono-complete"
command -v mono >/dev/null 2>&1 || fail "mono not found — install mono-complete"

# ---- 1. Compile ----------------------------------------------------------
step "Compile game (mcs -warn:4)"
mkdir -p BasicGuessingGame/bin
mcs -warn:4 -out:"$GAME_BIN" $GAME_SOURCES || fail "game compile"

step "Compile tests (mcs -warn:4)"
mkdir -p BasicGuessingGame.Tests/bin
mcs -warn:4 -out:"$TEST_BIN" $TEST_SOURCES || fail "test compile"

# ---- 2. Unit tests -------------------------------------------------------
step "Unit tests (deterministic via injected seed)"
mono "$TEST_BIN" || fail "unit tests"

# ---- 3. Smoke tests (mirror CI) -----------------------------------------
step "Smoke: invalid input is not silently treated as 0"
printf 'abc\n' | mono "$GAME_BIN" | grep -q 'Invalid number' || fail "invalid input"

step "Smoke: EOF exits cleanly"
printf '' | mono "$GAME_BIN" | grep -q 'Input closed. Goodbye!' || fail "EOF"

step "Smoke: guaranteed win (0-99, limit 200)"
seq 0 99 | mono "$GAME_BIN" 200 | grep -q 'Correct! You have guessed the number.' || fail "guaranteed win"

step "Smoke: high out-of-range guess is rejected, not 'too high'"
OUT=$(printf '150\n' | mono "$GAME_BIN" 50)
echo "$OUT" | grep -q 'Please enter a number between 0 and 99' || fail "high oob message"
! echo "$OUT" | grep -q 'too high' || fail "high oob was played as 'too high'"

step "Smoke: low out-of-range guess is rejected, not 'too low'"
OUT=$(printf '%s\n' '-5' | mono "$GAME_BIN" 50)
echo "$OUT" | grep -q 'Please enter a number between 0 and 99' || fail "low oob message"
! echo "$OUT" | grep -q 'too low' || fail "low oob was played as 'too low'"

step "Smoke: player can lose when the guess limit is hit"
found=0
for i in 1 2 3 4 5 6 7 8 9 10 11 12 13 14 15 16 17 18 19 20; do
  OUT=$(printf '0\n1\n2\n' | mono "$GAME_BIN" 3)
  if echo "$OUT" | grep -q 'ran out of guesses'; then
    echo "Observed the loss path on attempt $i"
    echo "$OUT" | grep -q 'The number was' || fail "loss did not reveal the number"
    found=1
    break
  fi
done
[ "$found" -eq 1 ] || fail "loss path not observed in 20 attempts"

step "Smoke: seeded round is deterministic (guaranteed loss, secret 24)"
OUT=$(printf '0\n1\n2\n' | mono "$GAME_BIN" 3 1)
echo "$OUT" | grep -q 'ran out of guesses' || fail "seeded loss"
echo "$OUT" | grep -q 'The number was 24' || fail "seeded secret is 24"

step "Smoke: seeded round is deterministic (guaranteed win on one guess)"
OUT=$(printf '24\n' | mono "$GAME_BIN" 10 1)
echo "$OUT" | grep -q 'Correct! You have guessed the number.' || fail "seeded win"

step "Smoke: seeded custom range (issue #12: [min] [max], seed 1, 10-50, secret 20)"
OUT=$(printf '20\n' | mono "$GAME_BIN" 10 1 10 50)
echo "$OUT" | grep -q 'between 10-50' || fail "custom range banner"
echo "$OUT" | grep -q 'Correct! You have guessed the number.' || fail "custom range win"

step "Smoke: invalid guess limit falls back to 10 and says so"
OUT=$(printf '' | mono "$GAME_BIN" abc)
echo "$OUT" | grep -q 'is not a valid guess limit' || fail "invalid limit message"
echo "$OUT" | grep -q 'You have 10 guesses' || fail "invalid limit fell back to 10"

step "Smoke: guess limit above the cap is clamped to 100 and says so"
OUT=$(printf '' | mono "$GAME_BIN" 999999999)
echo "$OUT" | grep -q 'above the maximum of 100' || fail "clamp message"
echo "$OUT" | grep -q 'You have 100 guesses' || fail "limit clamped to 100"

step "Smoke: play-again prompt appears after a win"
seq 0 99 | mono "$GAME_BIN" 200 | grep -q 'Play again? (y/n)' || fail "play again"

step "ALL GREEN"
printf 'Local runner finished: all checks passed.\n'
exit 0
