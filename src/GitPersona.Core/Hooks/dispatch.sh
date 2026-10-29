# Managed by GitPersona: rewritten whenever the commit guard is installed. Do not edit.
#
# Sourced by every hook stub in GitPersona's global hooks folder (core.hooksPath), so $0 is the stub
# and "$@" its arguments. It
#   1. for pre-commit, checks that the author email matches the repository's GitPersona profile
#      (the profile of its remote SSH alias, otherwise of the deepest profile folder containing it);
#   2. runs the repository's own hook (.git/hooks/<name>) and the global hooks folder that was
#      configured before GitPersona, so husky v4, pre-commit, Git LFS and hand-written hooks keep working.
#
# Windows starts processes slowly and git runs several hooks per command, so the common path uses
# shell builtins only; the identity check costs one git call and one awk.

gp_hook=${0##*/}
gp_dir=${0%/*}/_gitpersona
gp_nl='
'
. "$gp_dir/guard.conf"

# Hooks run in the top of the work tree (or in $GIT_DIR for bare repositories).
if [ -d .git ]; then
    gp_common=.git
else
    gp_common=$(git rev-parse --git-common-dir 2>/dev/null)
fi

gp_check_identity() {
    [ -f "$gp_dir/rules.tsv" ] || return 0

    gp_top=$(pwd -W 2>/dev/null) || gp_top=$(git rev-parse --show-toplevel 2>/dev/null)

    # One git call yields the remotes and the author email; awk applies git's precedence
    # (GIT_AUTHOR_EMAIL, author.email, user.email, EMAIL) and matches the rules.
    # Prints "none", "ok", "conflict" or "block<NL>expected email<NL>profile name<NL>current email".
    # LC_ALL=C keeps tolower() to ASCII, matching how GitPersona normalises the rules.
    gp_verdict=$(git config --get-regexp '^(remote\..*\.url|user\.email|author\.email)$' 2>/dev/null | LC_ALL=C awk -F '\t' \
        -v top="$gp_top/" -v env_author="${GIT_AUTHOR_EMAIL-}" -v env_email="${EMAIL-}" -v has_env_author="${GIT_AUTHOR_EMAIL+1}" '
        function host(u,   h) {
            if (u ~ /^ssh:\/\//) { h = substr(u, 7); sub(/^[^@\/]*@/, "", h); sub(/[\/:].*$/, "", h); return tolower(h) }
            if (u ~ /:\/\//) return ""
            if (u ~ /:/) { h = u; sub(/:.*$/, "", h); sub(/^.*@/, "", h); return tolower(h) }
            return ""
        }
        function alias_rule(h,   i) {
            for (i = 1; i <= n; i++) if (kind[i] == "alias" && key[i] == h) return i
            return ""
        }
        FNR == NR { n++; kind[n] = $1; key[n] = tolower($2); want_email[n] = $3; name[n] = $4; next }
        {
            space = index($0, " ")
            k = space ? substr($0, 1, space - 1) : $0
            v = space ? substr($0, space + 1) : ""
            if (k == "user.email") { user_email = v; next }
            if (k == "author.email") { author_email = v; next }
            h = host(v)
            if (h == "") next
            if (k == "remote.origin.url") origin = h; else hosts[++hn] = h
        }
        END {
            email = has_env_author != "" ? env_author : author_email != "" ? author_email : user_email != "" ? user_email : env_email

            by_alias = origin != "" ? alias_rule(origin) : ""
            for (i = 1; by_alias == "" && i <= hn; i++) by_alias = alias_rule(hosts[i])

            ltop = tolower(top); deepest = 0; by_folder = ""
            for (i = 1; i <= n; i++)
                if (kind[i] == "folder" && index(ltop, key[i] "/") == 1 && length(key[i]) > deepest) { deepest = length(key[i]); by_folder = i }

            rule = by_alias != "" ? by_alias : by_folder
            if (rule == "") { print "none"; exit }
            if (tolower(email) != tolower(want_email[rule])) { print "block"; print want_email[rule]; print name[rule]; print email; exit }
            print (by_alias != "" && by_folder != "" && name[by_alias] != name[by_folder]) ? "conflict" : "ok"
        }' "$gp_dir/rules.tsv" -)

    case $gp_verdict in
        block*)
            gp_rest=${gp_verdict#*"$gp_nl"}
            gp_expected_email=${gp_rest%%"$gp_nl"*}
            gp_rest=${gp_rest#*"$gp_nl"}
            gp_expected_name=${gp_rest%%"$gp_nl"*}
            gp_email=
            case $gp_rest in *"$gp_nl"*) gp_email=${gp_rest#*"$gp_nl"} ;; esac
            {
                printf '\n%s\n\n' "$GP_MSG_BLOCKED"
                printf '  %s %s\n' "$GP_MSG_PROFILE" "$gp_expected_name"
                printf '  %s %s\n' "$GP_MSG_EXPECTED" "$gp_expected_email"
                printf '  %s %s\n\n' "$GP_MSG_CURRENT" "${gp_email:-$GP_MSG_NOT_SET}"
                printf '%s\n' "$GP_MSG_FIX"
                printf '    git config user.email "%s"\n\n' "$gp_expected_email"
                printf '%s\n' "$GP_MSG_BYPASS"
                printf '    git commit --no-verify\n\n'
            } >&2
            return 1 ;;
        conflict)
            printf 'GitPersona: %s\n' "$GP_MSG_CONFLICT" >&2 ;;
    esac
    return 0
}

# Git runs a hook only when the file starts with "#!" (or is an .exe); mirror that without spawning anything.
gp_runnable() {
    [ -f "$1" ] || return 1
    case $1 in *.exe) return 0 ;; esac
    IFS= read -r gp_first_line < "$1" || [ -n "$gp_first_line" ] || return 1
    case $gp_first_line in '#!'*) return 0 ;; esac
    return 1
}

if [ "$gp_hook" = pre-commit ] && [ "$GP_GUARD_ENABLED" = 1 ]; then
    gp_check_identity || exit 1
fi

gp_run1=
gp_run2=
if [ -n "$gp_common" ] && gp_runnable "$gp_common/hooks/$gp_hook"; then
    gp_run1=$gp_common/hooks/$gp_hook
fi
if [ -n "$GP_PREVIOUS_HOOKS_PATH" ] && gp_runnable "$GP_PREVIOUS_HOOKS_PATH/$gp_hook"; then
    if [ -z "$gp_run1" ]; then gp_run1=$GP_PREVIOUS_HOOKS_PATH/$gp_hook; else gp_run2=$GP_PREVIOUS_HOOKS_PATH/$gp_hook; fi
fi

if [ -z "$gp_run1" ]; then
    # Git LFS keeps its hooks in the hooks folder; under core.hooksPath a repository may have none of its own.
    case $gp_hook in
        pre-push|post-checkout|post-commit|post-merge)
            if [ -d "$gp_common/lfs" ] && command -v git-lfs >/dev/null 2>&1; then
                exec git lfs "$gp_hook" "$@"
            fi ;;
    esac
    exit 0
fi

if [ -z "$gp_run2" ]; then
    exec "$gp_run1" "$@"
fi

# Two hooks: both need the same stdin (pre-push and post-rewrite read it), so keep a copy.
gp_stdin=$(mktemp)
trap 'rm -f "$gp_stdin"' EXIT
cat > "$gp_stdin"
"$gp_run1" "$@" < "$gp_stdin" || exit $?
"$gp_run2" "$@" < "$gp_stdin"
