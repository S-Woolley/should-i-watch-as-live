# Spec Delta

## Purpose

Lets visitors move between the site's sport sections from a consistent navigation menu, with each section reachable at its own URL.

## ADDED Requirements

### Requirement: Navigation menu on every page
The site SHALL show a navigation menu on every page. The menu SHALL contain one item, "College Football", which links to the College Football page.

#### Scenario: Menu is visible on the home page
- **WHEN** a visitor opens the home page
- **THEN** the navigation menu is shown with a "College Football" item

#### Scenario: Menu is visible on the College Football page
- **WHEN** a visitor is on the College Football page
- **THEN** the navigation menu is still shown

#### Scenario: Menu item opens College Football
- **WHEN** a visitor selects the "College Football" menu item
- **THEN** the College Football page is shown and the browser URL path is `/college-football`

### Requirement: Current section is indicated
The navigation menu SHALL mark the item for the section the visitor is currently viewing as the current page, both visually and to assistive technology.

#### Scenario: College Football marked as current
- **WHEN** a visitor is on any `/college-football` URL
- **THEN** the "College Football" menu item is marked as the current page (for example with `aria-current="page"`)

#### Scenario: Nothing marked on the home page
- **WHEN** a visitor is on the home page
- **THEN** no menu item is marked as the current page

### Requirement: Sections are directly addressable
Each section SHALL be reachable by opening its URL directly or by refreshing the page, not only by navigating from the home page.

#### Scenario: Deep link to College Football
- **WHEN** a visitor opens `/college-football` directly in a new browser tab
- **THEN** the College Football page is shown with the navigation menu

#### Scenario: Unknown path
- **WHEN** a visitor opens a path that matches no section
- **THEN** the site shows a "page not found" message with the navigation menu, so the visitor can continue to College Football

### Requirement: Home page points to available sports
The home page SHALL briefly explain what the site does and SHALL link to the College Football page. The home page SHALL NOT depend on any game data to render.

#### Scenario: Home page content
- **WHEN** a visitor opens the home page
- **THEN** they see a short description of the site and a link to College Football
