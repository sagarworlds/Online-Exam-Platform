import { vi } from 'vitest';
import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { ADMIN_SECTION_GROUPS, AdminSectionGroup, SETTINGS_PAGES, SETTINGS_SECTION } from '../../auth/admin-sections';
import { AdminSidebar } from './admin-sidebar';

@Component({ template: '' })
class Blank {}

const groups: AdminSectionGroup[] = [
  {
    heading: 'admin.group.content',
    sections: [{ label: 'admin.area.questions.name', description: 'admin.area.questions.description', path: '/admin/questions', permissions: ['question.manage'], icon: 'question' }],
  },
  {
    heading: 'admin.group.people',
    sections: [{ label: 'admin.area.students.name', description: 'admin.area.students.description', path: '/students', permissions: ['student.read'], icon: 'student' }],
  },
  { heading: null, sections: [SETTINGS_SECTION] },
];

describe('AdminSidebar', () => {
  let fixture: ComponentFixture<AdminSidebar>;
  let root: HTMLElement;

  function render(menu: readonly AdminSectionGroup[]): void {
    fixture = TestBed.createComponent(AdminSidebar);
    fixture.componentRef.setInput('groups', menu);
    fixture.detectChanges();
    root = fixture.nativeElement as HTMLElement;
  }

  const links = () => Array.from(root.querySelectorAll<HTMLAnchorElement>('.admin-sidebar__link'));
  const labels = () => links().map((a) => a.querySelector('.admin-sidebar__label')?.textContent?.trim());
  const sidebarGroups = () => Array.from(root.querySelectorAll<HTMLElement>('.admin-sidebar__group'));
  const toggle = () => root.querySelector('button')!;

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [AdminSidebar],
      providers: [provideRouter([{ path: '**', component: Blank }])],
    }).compileComponents();
    render(groups);
  });

  it('renders one link per section, labelled and reachable', () => {
    expect(labels()).toEqual(['Questions', 'Students', 'Settings']);
    expect(links().map((a) => a.getAttribute('href'))).toEqual(['/admin/questions', '/students', '/admin/settings']);
  });

  it('starts expanded', () => {
    expect(root.querySelector('.admin-sidebar--collapsed')).toBeNull();
    expect(toggle().getAttribute('aria-expanded')).toBe('true');
  });

  describe('groups', () => {
    it('draws each group under its heading, in the order given', () => {
      expect(sidebarGroups().map((group) => group.querySelector('.admin-sidebar__heading')?.textContent?.trim() ?? null)).toEqual(['Content', 'People', null]);
      expect(sidebarGroups().map((group) => Array.from(group.querySelectorAll('.admin-sidebar__link')).length)).toEqual([1, 1, 1]);
    });

    it('names each headed group by its heading, which is real text a screen reader can read', () => {
      const [content, people] = sidebarGroups();

      for (const group of [content, people]) {
        const heading = group.querySelector<HTMLElement>('.admin-sidebar__heading')!;

        expect(group.getAttribute('role')).toBe('group');
        expect(heading.id).not.toBe('');
        expect(group.getAttribute('aria-labelledby')).toBe(heading.id);
        expect(heading.getAttribute('aria-hidden')).toBeNull();
        expect(heading.hidden).toBe(false);
      }
    });

    it('gives each heading an id of its own, so every group is named by its own heading', () => {
      const ids = Array.from(root.querySelectorAll('.admin-sidebar__heading')).map((heading) => heading.id);

      expect(new Set(ids).size).toBe(ids.length);
    });

    it('keeps a heading a plain label rather than a control or an outline heading', () => {
      const heading = root.querySelector('.admin-sidebar__heading')!;

      expect(heading.closest('a, button')).toBeNull();
      expect(root.querySelector('h1, h2, h3, h4, h5, h6')).toBeNull();
    });

    it('draws the group that has no heading as a bare run of links, with no empty label for it', () => {
      const bare = sidebarGroups()[2];

      expect(bare.querySelector('.admin-sidebar__heading')).toBeNull();
      expect(bare.getAttribute('role')).toBeNull();
      expect(bare.getAttribute('aria-labelledby')).toBeNull();
      expect(bare.querySelector('a')?.getAttribute('href')).toBe('/admin/settings');
    });

    it('draws the whole menu: every group of the real one, with its headings', () => {
      render(ADMIN_SECTION_GROUPS);

      expect(Array.from(root.querySelectorAll('.admin-sidebar__heading')).map((heading) => heading.textContent?.trim())).toEqual([
        'Content',
        'Exams',
        'People',
        'Reviews',
      ]);
      expect(labels()).toEqual([
        'Questions',
        'Books',
        'Exams',
        'Students',
        'Batches',
        'Guardians',
        'Attempt requests',
        'Disputes',
        'Reported issues',
        'Settings',
      ]);
    });

    it('draws nothing but the toggle for a menu with no group', () => {
      render([]);

      expect(sidebarGroups()).toEqual([]);
      expect(links()).toEqual([]);
    });
  });

  describe('the count of what is waiting', () => {
    const reviews: AdminSectionGroup[] = [
      {
        heading: 'admin.group.reviews',
        sections: [
          { label: 'admin.area.attemptRequests.name', description: 'admin.area.attemptRequests.description', path: '/admin/attempt-requests', permissions: ['exam.manage'], icon: 'attempt-request', attention: 'attemptRequests' },
          { label: 'admin.area.disputes.name', description: 'admin.area.disputes.description', path: '/admin/disputes', permissions: ['exam.manage'], icon: 'dispute', attention: 'disputes' },
          { label: 'admin.area.students.name', description: 'admin.area.students.description', path: '/students', permissions: ['student.read'], icon: 'student' },
        ],
      },
    ];
    const badges = () => links().map((a) => a.querySelector('.admin-sidebar__badge')?.textContent?.trim() ?? null);

    function showing(attention: Record<string, unknown>): void {
      render(reviews);
      fixture.componentRef.setInput('attention', attention);
      fixture.detectChanges();
    }

    it('shows how many wait beside the entry of each queue that has something waiting, and nothing on one that is clear', () => {
      showing({ attemptRequests: { status: 'ready', count: 3, atLimit: false }, disputes: { status: 'ready', count: 0, atLimit: false } });

      expect(badges()).toEqual(['3', null, null]);
    });

    it('says "200+" when the queue reached the most its list holds', () => {
      showing({ attemptRequests: { status: 'ready', count: 200, atLimit: true } });

      expect(badges()[0]).toBe('200+');
    });

    it('shows nothing while a count is not known, or could not be read, rather than a zero', () => {
      showing({ attemptRequests: { status: 'loading' }, disputes: { status: 'failed' } });

      expect(badges()).toEqual([null, null, null]);
    });

    it('puts the number in the link’s name, in words, and keeps the figure itself from being read twice', () => {
      showing({ attemptRequests: { status: 'ready', count: 3, atLimit: false }, disputes: { status: 'ready', count: 200, atLimit: true } });

      expect(links().map((a) => a.getAttribute('aria-label'))).toEqual(['Attempt requests, 3 waiting', 'Disputes, 200 or more waiting', 'Students']);
      expect(links()[0].querySelector('.admin-sidebar__badge')?.getAttribute('aria-hidden')).toBe('true');
    });

    it('has no badge on any entry when it is given no counts', () => {
      render(reviews);

      expect(badges()).toEqual([null, null, null]);
    });
  });

  describe('on a phone, where the menu is one row until it is opened', () => {
    const menuButton = () => root.querySelector<HTMLButtonElement>('button.admin-sidebar__menu')!;

    it('has a row that opens the groups, shut to begin with, and says which block it opens', () => {
      expect(menuButton().textContent).toContain('Admin menu');
      expect(menuButton().getAttribute('aria-expanded')).toBe('false');
      expect(root.querySelector('.admin-sidebar--phone-open')).toBeNull();
      expect(menuButton().getAttribute('aria-controls')).toBe(root.querySelector('.admin-sidebar__groups')?.id);
    });

    it('opens and shuts with the row', () => {
      menuButton().click();
      fixture.detectChanges();
      expect(menuButton().getAttribute('aria-expanded')).toBe('true');
      expect(root.querySelector('.admin-sidebar--phone-open')).not.toBeNull();

      menuButton().click();
      fixture.detectChanges();
      expect(menuButton().getAttribute('aria-expanded')).toBe('false');
    });

    it('folds away after a link is followed, so the page that was asked for is the one in view', async () => {
      menuButton().click();
      fixture.detectChanges();

      await TestBed.inject(Router).navigateByUrl('/students');
      fixture.detectChanges();

      expect(menuButton().getAttribute('aria-expanded')).toBe('false');
    });

    it('keeps every link in the page, so nothing changes for the other sizes, which never draw the row', () => {
      expect(labels()).toEqual(['Questions', 'Students', 'Settings']);
    });

    it('says on the row how many are waiting in all the queues, so a shut menu hides nothing that needs attention', () => {
      const reviews: AdminSectionGroup[] = [
        {
          heading: 'admin.group.reviews',
          sections: [
            { label: 'admin.area.attemptRequests.name', description: 'admin.area.attemptRequests.description', path: '/admin/attempt-requests', permissions: ['exam.manage'], icon: 'attempt-request', attention: 'attemptRequests' },
            { label: 'admin.area.disputes.name', description: 'admin.area.disputes.description', path: '/admin/disputes', permissions: ['exam.manage'], icon: 'dispute', attention: 'disputes' },
          ],
        },
      ];
      render(reviews);
      expect(menuButton().querySelector('.admin-sidebar__badge')).toBeNull();

      fixture.componentRef.setInput('attention', { attemptRequests: { status: 'ready', count: 3, atLimit: false }, disputes: { status: 'ready', count: 2, atLimit: false } });
      fixture.detectChanges();

      expect(menuButton().querySelector('.admin-sidebar__badge')?.textContent?.trim()).toBe('5');
      expect(menuButton().querySelector('.visually-hidden')?.textContent?.trim()).toBe('5 waiting');
    });
  });

  describe('collapsed', () => {
    beforeEach(() => {
      toggle().click();
      fixture.detectChanges();
    });

    it('keeps every link reachable and labelled, just without the visible text', () => {
      expect(root.querySelector('.admin-sidebar--collapsed')).not.toBeNull();
      expect(links().map((a) => a.getAttribute('href'))).toEqual(['/admin/questions', '/students', '/admin/settings']);
      expect(links().map((a) => a.getAttribute('aria-label'))).toEqual(['Questions', 'Students', 'Settings']);
      expect(links().map((a) => a.getAttribute('title'))).toEqual(['Questions', 'Students', 'Settings']);
      expect(toggle().getAttribute('aria-expanded')).toBe('false');
    });

    it('keeps the groups and their headings in the page, hidden from the eye only, so each group is still named', () => {
      const [content, people] = sidebarGroups();

      expect(Array.from(root.querySelectorAll('.admin-sidebar__heading')).map((heading) => heading.textContent?.trim())).toEqual(['Content', 'People']);
      for (const group of [content, people]) {
        const heading = group.querySelector<HTMLElement>('.admin-sidebar__heading')!;

        expect(heading.hidden).toBe(false);
        expect(heading.getAttribute('aria-hidden')).toBeNull();
        expect(group.getAttribute('aria-labelledby')).toBe(heading.id);
      }
    });

    it('is the same menu when expanded again', () => {
      toggle().click();
      fixture.detectChanges();

      expect(root.querySelector('.admin-sidebar--collapsed')).toBeNull();
      expect(labels()).toEqual(['Questions', 'Students', 'Settings']);
      expect(root.querySelectorAll('.admin-sidebar__heading').length).toBe(2);
    });
  });

  it('remembers the collapsed state for the next render', () => {
    toggle().click();
    fixture.detectChanges();

    const second = TestBed.createComponent(AdminSidebar);
    second.componentRef.setInput('groups', groups);
    second.detectChanges();

    expect((second.nativeElement as HTMLElement).querySelector('.admin-sidebar--collapsed')).not.toBeNull();
  });

  describe('on a tablet’s width, where the page is worth more than the labels', () => {
    const fresh = () => {
      const created = TestBed.createComponent(AdminSidebar);
      created.componentRef.setInput('groups', groups);
      created.detectChanges();
      return created.nativeElement as HTMLElement;
    };
    const width = (tablet: boolean) =>
      vi.stubGlobal('matchMedia', (query: string) => ({ matches: tablet && query === '(max-width: 64rem)', media: query }));
    afterEach(() => vi.unstubAllGlobals());

    it('starts as a rail of icons when the visitor has not chosen', () => {
      width(true);

      expect(fresh().querySelector('.admin-sidebar--collapsed')).not.toBeNull();
    });

    it('starts open on a wide screen', () => {
      width(false);

      expect(fresh().querySelector('.admin-sidebar--collapsed')).toBeNull();
    });

    it('keeps what the visitor chose, whatever the width', () => {
      width(true);
      localStorage.setItem('exam-platform.admin-sidebar-collapsed', '0');

      expect(fresh().querySelector('.admin-sidebar--collapsed')).toBeNull();
    });
  });

  describe('the current section', () => {
    async function visit(url: string): Promise<void> {
      await TestBed.inject(Router).navigateByUrl(url);
      fixture.detectChanges();
    }

    const currentLinks = () => links().filter((a) => a.classList.contains('active')).map((a) => a.getAttribute('href'));

    it('marks nothing before any page has opened', () => {
      expect(currentLinks()).toEqual([]);
    });

    it('marks the section whose page the user is on', async () => {
      await visit('/students');

      expect(currentLinks()).toEqual(['/students']);
    });

    it('keeps the section marked on a page below its own, such as the exam editor below Exams', async () => {
      render(ADMIN_SECTION_GROUPS);
      await visit('/exams/abc/schedule');

      expect(currentLinks()).toEqual(['/exams']);
    });

    it('moves the mark when the user goes elsewhere', async () => {
      await visit('/students');
      await visit('/admin/questions');

      expect(currentLinks()).toEqual(['/admin/questions']);
    });

    it('marks Settings on the Settings page itself', async () => {
      await visit('/admin/settings');

      expect(currentLinks()).toEqual(['/admin/settings']);
    });

    it.each(SETTINGS_PAGES.map((page) => page.path))('marks Settings, and nothing else, on its page %s', async (path) => {
      await visit(path);

      expect(currentLinks()).toEqual(['/admin/settings']);
    });

    it('does not mark Settings on a page that is not one of its own', async () => {
      await visit('/admin/questions');

      expect(currentLinks()).toEqual(['/admin/questions']);
    });
  });
});
