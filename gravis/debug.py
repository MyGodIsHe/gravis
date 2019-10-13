from collections import defaultdict
from contextlib import ContextDecorator
from enum import Enum
from io import StringIO
from typing import NamedTuple, List, Tuple

from .base import Node
from .nodes import *
from . import events
from . import globals

__all__ = (
    'DebugContext',
)


@events.event_activate
def log_forward(self, *args):
    debug = globals.current_debug()
    if debug:
        values = args[:-1]
        src_node = args[-1]
        debug.iterations.append(Iteration(
            src=NodeName.create(src_node),
            dst=NodeName.create(self),
            direction=Direction.forward,
            args=values,
        ))


@events.event_activate_me
def log_backward(self, *args):
    debug = globals.current_debug()
    if debug:
        values = args[:-1]
        src_node = args[-1]
        debug.iterations.append(Iteration(
            src=NodeName.create(src_node),
            dst=NodeName.create(self),
            direction=Direction.backward,
            args=values,
        ))


def get_uuid(node, prefix=None):
    if not prefix:
        prefix = str(node).lower()
    return '{}_{}'.format(prefix, hex(id(node))[2:])


class Direction(Enum):
    backward = False
    forward = True


class NodeName(NamedTuple):
    label: str
    id: str
    node: Node

    OPERATOR_MAP = {
        'le': '<=',
        'add': '+',
        'sub': '-',
    }

    SHAPE_MAP = {
        'Input': 'square',
        'Output': 'square',
        'Constant': 'circle',
        'If': 'diamond',
        'Operator': 'diamond',
    }

    @property
    def shape(self):
        return self.SHAPE_MAP.get(str(self.node), 'box')

    @staticmethod
    def resolve_operator(value):
        return NodeName.OPERATOR_MAP.get(value, value)

    @staticmethod
    def create(node: Node) -> 'NodeName':
        if isinstance(node, Input):
            label = 'I'
        elif isinstance(node, Output):
            label = 'O'
        elif isinstance(node, Constant):
            label = str(node.value)
        elif isinstance(node, If):
            label = NodeName.resolve_operator(node.operator.__name__)
        elif isinstance(node, Operator):
            label = NodeName.resolve_operator(node.operator.__name__)
        else:
            label = str(node)

        return NodeName(
            label=label,
            id=get_uuid(node),
            node=node,
        )


class Iteration(NamedTuple):
    src: NodeName
    dst: NodeName
    args: Tuple[NodeName]
    direction: Direction

    @property
    def style(self):
        return 'solid' if self.direction == Direction.forward else 'dotted'

    @property
    def label_args(self):
        if self.args:
            return ', '.join(str(arg) for arg in self.args)
        return ''

    def render(self, step):
        label_args = self.label_args
        if label_args:
            label = r'[{}]\n{}'.format(step, label_args)
        else:
            label = '[{}]'.format(step)
        return (
            '\t{src} -> {dst} '
            '[label="{label}";style={style}];'
            '\n'.format(
                src=self.src.id,
                dst=self.dst.id,
                label=label,
                style=self.style,
            )
        )


def around_generator(items: List[Iteration]):
    items = iter(items)
    past = None
    current = next(items, None)
    future = next(items, None)
    while current:
        yield past, current, future
        past = current
        current = future
        future = next(items, None)


def subspace_bridge(past: Iteration, current: Iteration, future: Iteration):
    if current.direction == Direction.forward:
        if isinstance(current.dst.node, Subspace):
            return Iteration(
                src=current.src,
                dst=future.dst,
                args=current.args,
                direction=current.direction,
            )
        if isinstance(current.src.node, Subspace):
            return Iteration(
                src=past.dst,
                dst=current.dst,
                args=current.args,
                direction=current.direction,
            )


def filter_iterations(iterations: List[Iteration]):
    filtered = []
    for past, current, future in around_generator(iterations):
        if current.src.node is None:
            continue
        if current.direction == Direction.forward:
            if isinstance(current.dst.node, Subspace):
                continue
            if isinstance(current.src.node, Subspace):
                continue
            if (
                    isinstance(current.dst.node, Output) and
                    future and isinstance(future.src.node, Subspace)
            ):
                filtered.append(current)
                filtered.append(Iteration(
                    src=current.dst,
                    dst=future.dst,
                    args=current.args,
                    direction=Direction.forward,
                ))
                continue
        else:
            if isinstance(current.dst.node, Subspace):
                filtered.append(Iteration(
                    src=current.src,
                    dst=future.dst,
                    args=current.args,
                    direction=Direction.backward,
                ))
                continue
            if isinstance(current.src.node, Subspace):
                continue
        filtered.append(current)

    return filtered


def collect_links(start_node: Node):
    pass_nodes = set()
    links = set()
    coming_nodes = {start_node}

    while coming_nodes:
        current = coming_nodes.pop()
        pass_nodes.add(current)
        linked_nodes = set(current.in_nodes + current.out_nodes) - pass_nodes
        coming_nodes |= linked_nodes
        if isinstance(current, Subspace):
            continue
        for other in linked_nodes:
            if isinstance(other, Subspace):
                continue
            if id(current) > id(other):
                links.add((current, other))
            else:
                links.add((other, current))

    return links, pass_nodes


def collect_levels(node: Node, level_map, all_nodes, level=0):
    if node not in all_nodes:
        return
    if node in level_map[level]:
        return
    level_map[level].add(node)
    for obj in node.out_nodes:
        collect_levels(obj, level_map, all_nodes, level + 1)
    for obj in node.in_nodes:
        collect_levels(obj, level_map, all_nodes, level - 1)


def write_rank(stream, level_map):
    for level_nodes in level_map.values():
        if len(level_nodes) > 1:
            stream.write(
                '\t{{rank=same {}}}\n'.format(
                    ' '.join(
                        '{}'.format(get_uuid(node))
                        for node in level_nodes
                    )
                )
            )


def get_subspace_map(node_names):
    subspace_nodes = defaultdict(set)
    subspace_children = defaultdict(set)
    for node_name in node_names:
        if node_name.node.subspace:
            subspace_children[node_name.node.subspace.subspace].add(
                node_name.node.subspace
            )
            subspace_nodes[node_name.node.subspace].add(node_name.node)

    return subspace_nodes, subspace_children


def write_subspaces(stream, subspaces, subspace_nodes, subspace_children):
    for subspace in subspaces:
        stream.write(
            '\tsubgraph {} {{\n'.format(get_uuid(subspace, 'cluster'))
        )
        start_node = None
        for node in subspace_nodes[subspace]:
            if isinstance(node, Input):
                start_node = node
            stream.write('\t\t{};\n'.format(get_uuid(node)))

        level_map = defaultdict(set)
        collect_levels(start_node, level_map, subspace_nodes[subspace])
        write_rank(stream, level_map)

        write_subspaces(
            stream,
            subspace_children[subspace],
            subspace_nodes,
            subspace_children,
        )
        stream.write('\t}\n')


class DebugContext(ContextDecorator):

    def __init__(self):
        self.iterations: List[Iteration] = []

    def create_digraph(self):
        stream = StringIO()
        stream.write('digraph {\n')
        stream.write('\tnewrank = true;\n')

        if self.iterations:
            iterations = filter_iterations(self.iterations)

            for step, iteration in enumerate(iterations):
                stream.write(iteration.render(step + 1))

            start_node = iterations[0].src.node
            all_links, all_nodes = collect_links(start_node)
            activated_links = {
                (
                    (iteration.src.node, iteration.dst.node)
                    if id(iteration.src.node) > id(iteration.dst.node)
                    else (iteration.dst.node, iteration.src.node)
                )
                for iteration in iterations
            }
            pass_links = all_links - activated_links
            pass_links = {
                (NodeName.create(src), NodeName.create(dst))
                for src, dst in pass_links
            }

            all_node_names = set()
            for iteration in iterations:
                all_node_names.add(iteration.src)
                all_node_names.add(iteration.dst)
            for src, dst in pass_links:
                all_node_names.add(src)
                all_node_names.add(dst)

            for src, dst in pass_links:
                stream.write((
                    '\t{src} -> {dst} [arrowhead=none];'
                    '\n'.format(
                        src=src.id,
                        dst=dst.id,
                    )
                ))

            for node_name in all_node_names:
                stream.write(
                    '\t{} [label="{}";shape={}];\n'.format(
                        node_name.id,
                        node_name.label,
                        node_name.shape,
                    )
                )

            subspace_nodes, subspace_children = get_subspace_map(all_node_names)
            write_subspaces(
                stream,
                subspace_children[None],
                subspace_nodes,
                subspace_children,
            )

        stream.write('}\n')
        return stream.getvalue()

    def __enter__(self):
        globals.DEBUG_STACK.append(self)
        return self

    def __exit__(self, *exc):
        globals.DEBUG_STACK.pop()
