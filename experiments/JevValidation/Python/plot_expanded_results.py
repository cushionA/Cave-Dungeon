"""Draw the reviewed primary estimates and their prespecified intervals."""
import argparse
import json
from pathlib import Path


def plot(stats_path, output_prefix):
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    from matplotlib.ticker import PercentFormatter, FuncFormatter

    stats = json.loads(Path(stats_path).read_text())
    if stats["first_model"] != "hindsight" or stats["second_model"] != "tinyjev":
        raise ValueError("The model labels do not match the reviewed comparison")
    groups = [stats["tasks"][task]["primary_ja"] for task in ("memory", "commander")]
    if any(group["worlds"] != 500 for group in groups):
        raise ValueError("This figure is for the fixed 500-world primary design")
    fig, axes = plt.subplots(1, 2, figsize=(11.2, 4.1), gridspec_kw={"width_ratios": [1.1, 1]})
    colors = {"first": "#146BA2", "second": "#606B76"}
    for y, group in zip((1, 0), groups):
        for key, offset, marker, label in (("first", .12, "o", "Hindsight 0.8B"),
                                           ("second", -.12, "^", "TinyJev 0.6B")):
            rate = group[key]["accuracy"]
            low, high = group[key]["wilson_95"]
            axes[0].errorbar(rate, y + offset, xerr=[[rate-low], [high-rate]],
                             color=colors[key], marker=marker, linestyle="none", capsize=4,
                             label=label if y == 1 else None)
            axes[0].annotate(f"{group[key]['correct']}/500 ({rate:.1%})", (rate, y+offset),
                             xytext=(0, 9 if offset > 0 else -14), textcoords="offset points",
                             ha="center", fontsize=9, color=colors[key])
        difference = group["difference_first_minus_second"]
        low, high = group["conservative_paired_difference_95"]
        axes[1].errorbar(difference, y, xerr=[[difference-low], [high-difference]],
                         marker="o", color=colors["first"], capsize=4, linestyle="none")
        p = group["holm_adjusted_p_two_primary_tasks"]
        axes[1].annotate(f"{100*difference:+.1f} pp; adjusted p={p:.2g}", (difference, y),
                         xytext=(0, 14), textcoords="offset points", ha="center", fontsize=9)
    axes[0].set(yticks=[1, 0], yticklabels=["Memory evidence", "Squad plans"],
                xlim=(0, 1.05), ylim=(-.5, 1.6), xlabel="Exact correctness (95% Wilson interval)")
    axes[0].xaxis.set_major_formatter(PercentFormatter(1))
    axes[0].legend(loc="upper left", frameon=False, fontsize=9)
    axes[1].axvline(0, color="#333333", linestyle="--", linewidth=1)
    axes[1].axvline(.1, color="#999999", linestyle=":", linewidth=1)
    lows = [group["conservative_paired_difference_95"][0] for group in groups]
    highs = [group["conservative_paired_difference_95"][1] for group in groups]
    axes[1].set(yticks=[1, 0], yticklabels=[], ylim=(-.5, 1.6),
                xlim=(min(-.03, min(lows)-.07), max(.15, max(highs)+.07)),
                xlabel="Hindsight - TinyJev (percentage points; paired 95% interval)")
    axes[1].xaxis.set_major_formatter(FuncFormatter(lambda value, _: f"{100*value:+.0f}"))
    for axis in axes:
        axis.grid(axis="x", color="#E5E9ED", linewidth=.7)
        axis.set_axisbelow(True)
        axis.spines[["top", "right"]].set_visible(False)
    fig.suptitle("Expanded NPC decision benchmark — Japanese primary inputs", fontsize=13)
    fig.text(.5, .02, "500 worlds/task; frozen synthetic generators, not gameplay outcomes. Holm correction across two paired tests.\n"
             "Dotted line: 10 percentage-point planning effect; translated English rows are secondary.",
             ha="center", fontsize=8, color="#4E5963")
    fig.tight_layout(rect=(0, .12, 1, .93))
    prefix = Path(output_prefix)
    prefix.parent.mkdir(parents=True, exist_ok=True)
    for extension in ("png", "svg"):
        fig.savefig(prefix.with_suffix("."+extension), dpi=200, facecolor="white")
    plt.close(fig)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--stats", type=Path, required=True)
    parser.add_argument("--output-prefix", type=Path, required=True)
    args = parser.parse_args()
    plot(args.stats, args.output_prefix)
