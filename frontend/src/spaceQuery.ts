type Space = { id: string; name: string };
type Query = Record<string, string | null | (string | null)[] | undefined>;
type SpaceResolution = { space: Space; error: '' } | { space: null; error: string };

/**
 * Vue Router has already decoded query values. Match them exactly, including
 * whitespace and literal percent signs. A space selector must be a single,
 * nonempty string; every array is rejected, even a one-element array.
 * Undefined values count as absent, as they do when writing a router query.
 */
export function resolveSpaceQuery(
  spaces: readonly Space[],
  query: Query,
  preferredId?: string,
): SpaceResolution {
  if (!spaces.length) return { space: null, error: '没有可用空间。' };

  if (query.spaceId !== undefined) {
    if (Array.isArray(query.spaceId)) {
      return { space: null, error: 'spaceId 参数只能指定一个空间，不能使用重复或数组值。' };
    }
    if (query.spaceId === null || query.spaceId === '') {
      return { space: null, error: 'spaceId 参数不能为空，请选择有效空间。' };
    }
    const matches = spaces.filter(space => space.id === query.spaceId);
    if (!matches.length) return { space: null, error: '未知空间 ID，请选择有效空间。' };
    if (matches.length > 1) return { space: null, error: '空间 ID 重复，无法确定目标空间。' };
    return { space: matches[0]!, error: '' };
  }

  if (query.space !== undefined) {
    if (Array.isArray(query.space)) {
      return { space: null, error: 'space 参数只能指定一个空间，不能使用重复或数组值。' };
    }
    if (query.space === null || query.space === '') {
      return { space: null, error: 'space 参数不能为空，请选择有效空间。' };
    }
    const matches = spaces.filter(space => space.name === query.space);
    if (!matches.length) return { space: null, error: '未知空间名称，请选择有效空间。' };
    if (matches.length > 1) {
      return { space: null, error: '空间名称重名，无法确定目标空间，请使用 spaceId。' };
    }
    return { space: matches[0]!, error: '' };
  }

  return { space: spaces.find(space => space.id === preferredId) ?? spaces[0]!, error: '' };
}

/**
 * Preserve unrelated query values and let Vue Router encode the raw name once.
 * Keep the selected ID when metadata says the name is ambiguous.
 */
export function spaceQuery(query: Query, space: Space, spaces?: readonly Space[]): Query {
  const next: Query = { ...query, space: space.name };
  if (spaces && spaces.filter(candidate => candidate.name === space.name).length > 1) {
    next.spaceId = space.id;
  } else {
    delete next.spaceId;
  }
  return next;
}
