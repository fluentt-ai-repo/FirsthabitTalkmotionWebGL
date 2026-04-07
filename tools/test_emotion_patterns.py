"""
EmotionKeywordDataset 패턴 테스트 도구
수학 강의 자막 텍스트에 대해 현재 패턴의 매칭 결과를 분석합니다.
"""
import re
import yaml
import sys
from collections import defaultdict

# --- Sample math lecture texts (Korean middle/high school) ---
SAMPLE_TEXTS = [
    # 1. 방정식 풀이
    (
        "자 이번에는 이차방정식을 풀어볼까요? x제곱 더하기 3x 빼기 4는 0이 됩니다. "
        "인수분해를 하면 x 더하기 4 곱하기 x 빼기 1이 되죠. "
        "따라서 x는 마이너스 4 또는 1이 됩니다. 꼭 기억하세요, 이차방정식의 근은 두 개입니다."
    ),
    # 2. 함수 개념 설명
    (
        "함수라는 건 뭘까요? 쉽게 말하면 입력을 넣으면 출력이 나오는 기계라고 생각하면 돼요. "
        "예를 들어 f(x) = 2x + 1이면, x에 3을 대입하면 7이 나오죠. "
        "중요한 건 하나의 입력에 하나의 출력만 있어야 한다는 거예요. 이게 핵심입니다."
    ),
    # 3. 확률 설명
    (
        "주사위를 던졌을 때 짝수가 나올 확률은 얼마일까요? "
        "전체 경우의 수는 6이고, 짝수는 2, 4, 6으로 3개죠. "
        "그러면 확률은 3 나누기 6, 즉 2분의 1이 됩니다. 간단하죠?"
    ),
    # 4. 도형/기하
    (
        "삼각형의 넓이 공식 기억나요? 밑변 곱하기 높이 나누기 2입니다. "
        "여기서 잠깐, 왜 나누기 2를 하는 걸까요? "
        "직사각형의 넓이에서 반을 자른 거라고 생각하면 돼요. "
        "이거 시험에 정말 자주 나옵니다. 반드시 외워두세요."
    ),
    # 5. 부등식
    (
        "부등식에서 주의해야 할 점이 있어요. "
        "양변에 음수를 곱하면 부등호 방향이 바뀝니다. 이건 절대 잊으면 안 돼요. "
        "예를 들어 마이너스 2x가 4보다 크다, 양변을 마이너스 2로 나누면 x는 마이너스 2보다 작다가 되죠. "
        "실수하면 안 되는 부분이에요."
    ),
    # 6. 인수분해
    (
        "그 뭐지, 인수분해 공식이 좀 헷갈리는데요. "
        "a제곱 빼기 b제곱은 a 더하기 b 곱하기 a 빼기 b, 이거 알죠? "
        "근데 a세제곱 더하기 b세제곱은 어떻게 될까요? "
        "이건 좀 복잡한데, 차근차근 풀어볼게요."
    ),
    # 7. 수열
    (
        "등차수열의 일반항 공식은 a sub n은 a sub 1 더하기 n 빼기 1 곱하기 d입니다. "
        "여기서 d는 공차예요. 첫째항이 3이고 공차가 2면, "
        "다섯 번째 항은 3 더하기 4 곱하기 2, 즉 11이 됩니다. "
        "이 공식 하나만 기억하면 등차수열 문제는 다 풀 수 있어요."
    ),
    # 8. 통계
    (
        "평균을 구하려면 모든 값을 더한 다음 개수로 나누면 됩니다. "
        "3, 5, 7, 9, 11의 평균은? 전부 더하면 35, 5로 나누면 7이죠. "
        "별거 아니죠? 하지만 중앙값은 좀 다릅니다. "
        "데이터를 크기순으로 정리하면 가운데 값이 중앙값이에요."
    ),
    # 9. 증명/논리
    (
        "자 그러면 이걸 증명해볼까요? 귀류법을 사용하겠습니다. "
        "루트 2가 유리수라고 가정해봅시다. 그러면 a 나누기 b로 나타낼 수 있죠. "
        "양변을 제곱하면 2는 a제곱 나누기 b제곱이 됩니다. "
        "따라서 a제곱은 2b제곱이 되고, a는 짝수가 되죠. 모순이 발생합니다."
    ),
    # 10. 격려/동기부여
    (
        "여러분 잘 따라오고 있어요. 이 부분이 어려운 건 당연해요. "
        "처음부터 잘하는 사람은 없어요. 한번 더 해보자. "
        "틀려도 괜찮아요. 틀린 게 아니라 배우는 과정인 거예요. "
        "자 다시 집중해볼까요?"
    ),
    # 11. 미적분 (고등)
    (
        "미분이란 순간변화율을 구하는 거예요. "
        "f(x) = x제곱이면 f프라임(x)는 2x가 됩니다. "
        "왜 그런 거냐면, 극한의 정의에서 h가 0으로 갈 때의 값을 구하는 거거든요. "
        "이 개념이 정말 중요합니다. 꼭 이해하고 넘어가세요."
    ),
    # 12. 짧은 전환/연결
    (
        "좋아요, 다음 문제로 넘어갈게요."
    ),
    # 13. 계산 과정
    (
        "자 계산해볼게요. 3 곱하기 7은 21이고, 여기에 4를 더하면 25가 되죠. "
        "25의 제곱근은 5입니다. 답은 5예요."
    ),
    # 14. 반복 강조
    (
        "다시 한번 강조할게요. 분모가 0이 되면 안 됩니다. "
        "이건 수학의 기본 중의 기본이에요. 분모에 0이 오면 정의되지 않아요. "
        "시험에서 이거 빠뜨리면 안 되는 거 알죠?"
    ),
    # 15. 비교/대조
    (
        "등차수열과 등비수열의 차이점을 정리해볼게요. "
        "등차수열은 같은 수를 더하고, 등비수열은 같은 수를 곱해요. "
        "헷갈리지 마세요. 완전 다른 개념이에요."
    ),
]


def parse_asset(filepath):
    """Parse Unity .asset YAML to extract emotion entries."""
    entries = []
    with open(filepath, "r", encoding="utf-8") as f:
        content = f.read()

    # Unity YAML uses custom tags - strip them for parsing
    lines = content.split("\n")
    in_entries = False
    current = None

    for line in lines:
        stripped = line.strip()
        if stripped == "entries:":
            in_entries = True
            continue
        if not in_entries:
            continue

        if stripped.startswith("- pattern:"):
            if current:
                entries.append(current)
            pattern_val = stripped[len("- pattern:"):].strip().strip('"')
            current = {"pattern": pattern_val, "emotionTag": "", "weight": 1.0}
        elif stripped.startswith("emotionTag:") and current:
            current["emotionTag"] = stripped[len("emotionTag:"):].strip().strip('"')
        elif stripped.startswith("weight:") and current:
            current["weight"] = float(stripped[len("weight:"):].strip())

    if current:
        entries.append(current)

    return entries


def unescape_yaml_string(s):
    """Unescape a YAML double-quoted string value.

    Handles:
    - \\uXXXX -> unicode character
    - \\\\ -> \\ (YAML double-backslash -> single backslash for regex \\s, \\b, etc.)
    """
    # Step 1: Convert \\uXXXX to unicode characters
    def replace_unicode(m):
        return chr(int(m.group(1), 16))
    s = re.sub(r'\\u([0-9a-fA-F]{4})', replace_unicode, s)

    # Step 2: Convert YAML escaped backslashes (\\s -> \s, \\b -> \b, etc.)
    # In YAML double-quoted strings, \\\\ means a literal backslash
    s = s.replace('\\\\', '\\')

    return s


def test_patterns(entries, texts):
    """Test all patterns against all texts and report matches."""
    # Group by emotion
    by_emotion = defaultdict(list)
    for e in entries:
        tag = unescape_yaml_string(e["emotionTag"])
        pattern = unescape_yaml_string(e["pattern"])
        by_emotion[tag].append({
            "pattern": pattern,
            "weight": e["weight"],
            "original": e["pattern"]
        })

    print(f"=== 감정 태그 {len(by_emotion)}개, 총 패턴 {len(entries)}개 ===\n")

    for i, text in enumerate(texts):
        print(f"\n{'='*80}")
        print(f"[텍스트 {i+1}] {text[:80]}...")
        print(f"{'='*80}")

        matches_found = []
        for tag, patterns in by_emotion.items():
            for p_info in patterns:
                try:
                    regex = re.compile(p_info["pattern"], re.IGNORECASE)
                    for m in regex.finditer(text):
                        matches_found.append({
                            "tag": tag,
                            "matched": m.group(),
                            "pos": m.start(),
                            "weight": p_info["weight"],
                            "pattern": p_info["pattern"][:50]
                        })
                except re.error as e:
                    print(f"  [REGEX ERROR] {tag}: {p_info['pattern'][:40]}... - {e}")

        # Sort by position
        matches_found.sort(key=lambda x: x["pos"])

        if not matches_found:
            print("  (매칭 없음)")
        else:
            # Summary by emotion
            tag_counts = defaultdict(int)
            for m in matches_found:
                tag_counts[m["tag"]] += 1

            print(f"\n  감정 요약: {dict(tag_counts)}")
            print(f"  총 매칭: {len(matches_found)}개\n")

            for m in matches_found:
                print(f"  [{m['tag']:4s}] w={m['weight']:<4} pos={m['pos']:3d} "
                      f"매칭=\"{m['matched']}\"  패턴={m['pattern']}")


def report_stats(entries):
    """Print pattern statistics."""
    by_emotion = defaultdict(list)
    for e in entries:
        tag = unescape_yaml_string(e["emotionTag"])
        pattern = unescape_yaml_string(e["pattern"])
        by_emotion[tag].append(pattern)

    print("\n=== 패턴 통계 ===\n")

    total_alts = 0
    for tag, patterns in sorted(by_emotion.items()):
        alts = sum(len(p.split("|")) for p in patterns)
        total_alts += alts
        print(f"  {tag}: {len(patterns)}개 항목, ~{alts}개 분기")

    print(f"\n  전체: {len(entries)}개 항목, ~{total_alts}개 분기")


if __name__ == "__main__":
    asset_path = sys.argv[1] if len(sys.argv) > 1 else \
        r"unity\Assets\EmotionKeywordDataset.asset"

    entries = parse_asset(asset_path)
    print(f"파싱 완료: {len(entries)}개 항목\n")

    report_stats(entries)
    print("\n")
    test_patterns(entries, SAMPLE_TEXTS)
